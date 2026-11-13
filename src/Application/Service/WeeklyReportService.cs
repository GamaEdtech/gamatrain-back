namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Data.Dto.WeeklyReport;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The weekly report: a plan feature (<see cref="FeatureCodes.WeeklyReport"/>). Every Monday the job sends each user
    /// whose plan includes it a report of last week (UTC, Monday to Sunday), or after a week with no activity a short
    /// reminder at most once every 4 weeks. Having the feature on the plan is the switch: it uses no quota, and a limit set
    /// on it is ignored. Built only from data this backend stores.
    /// Off unless <c>WeeklyReport:Enabled</c> is true, so sandbox never emails real users. See
    /// docs/business/notifications.md, "Weekly report".
    /// </summary>
    public sealed class WeeklyReportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<WeeklyReportService>> localizer, Lazy<ILogger<WeeklyReportService>> logger, Lazy<IEmailService> emailService
        , Lazy<IDataProtectionProvider> dataProtectionProvider, Lazy<IConfiguration> configuration, Lazy<ISubscriptionQuotaService> subscriptionQuotaService)
        : LocalizableServiceBase<WeeklyReportService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IWeeklyReportService
    {
        private const string UnsubscribeProtectorPurpose = "WeeklyReportUnsubscribe";
        private const int DefaultMaxSendsPerRun = 500;
        private const int MinRatedSchoolsForCityAverage = 3;

        public async Task<ResultData<WeeklyReportRunDto>> SendWeeklyReportsAsync()
        {
            try
            {
                var weekStart = WeeklyReportRules.LastFullWeek(DateTimeOffset.UtcNow);
                var run = new WeeklyReportRunDto { WeekStart = weekStart };
                if (!configuration.Value.GetValue("WeeklyReport:Enabled", defaultValue: false))
                {
                    return new(OperationResult.Succeeded) { Data = run };
                }

                var maxSends = configuration.Value.GetValue("WeeklyReport:MaxSendsPerRun", DefaultMaxSendsPerRun);
                var testRecipients = configuration.Value.GetSection("WeeklyReport:TestRecipients").Get<long[]>() ?? [];
                var candidates = await GetCandidatesAsync(weekStart, testRecipients, maxSends);
                run.Candidates = candidates.Count;

                foreach (var userId in candidates)
                {
                    try
                    {
                        var kind = await SendOneAsync(userId, weekStart);
                        if (kind == WeeklyReportKind.Report)
                        {
                            run.Reports++;
                        }
                        else if (kind == WeeklyReportKind.Reminder)
                        {
                            run.Reminders++;
                        }
                        else
                        {
                            run.Skipped++;
                        }
                    }
                    catch (Exception exc)
                    {
                        // One user's failure never stops the run; their week is retried by the next run (see SendOneAsync).
                        run.Failed++;
                        Logger.Value.LogException(exc);
                    }
                }

                return new(OperationResult.Succeeded) { Data = run };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<WeeklyReportPreviewDto>> GetPreviewAsync(long userId, DateOnly? weekOf)
        {
            try
            {
                var weekStart = weekOf is { } day ? WeeklyReportRules.WeekOf(day) : WeeklyReportRules.LastFullWeek(DateTimeOffset.UtcNow);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId)
                    .Select(t => new { t.Email, t.EmailConfirmed, t.WeeklyReportOptedOutAt }).FirstOrDefaultAsync();
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] },] };
                }

                var report = await BuildReportAsync(userId, weekStart);
                var hasActivity = WeeklyReportRules.HasActivity(report);
                var remind = !hasActivity && WeeklyReportRules.ShouldRemind(report.Plan, await GetLastReminderWeekAsync(userId, weekStart), weekStart);
                var alreadyHandled = await uow.GetRepository<WeeklyReportLog>().AnyAsync(t => t.UserId == userId && t.WeekStart == weekStart);

                string? reason = null;
                if (string.IsNullOrEmpty(user.Email) || !user.EmailConfirmed)
                {
                    reason = "The user has no confirmed email address.";
                }
                else if (user.WeeklyReportOptedOutAt is not null)
                {
                    reason = "The user turned the weekly report off.";
                }
                else if (!await HasFeatureAsync(userId))
                {
                    reason = "The user's current plan doesn't include the weekly report.";
                }
                else if (alreadyHandled)
                {
                    reason = "This week was already handled for the user.";
                }
                else if (!hasActivity && !remind)
                {
                    reason = "No activity this week, and no reminder is due.";
                }

                var links = GetLinks(userId);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        HasActivity = hasActivity,
                        WouldSend = reason is null,
                        NotSentReason = reason,
                        Email = remind ? WeeklyReportEmailBuilder.Reminder(report, links) : WeeklyReportEmailBuilder.Report(report, links),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<WeeklyReportSettingsDto>> GetSettingsAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId)
                    .Select(t => new { t.Email, t.EmailConfirmed, t.WeeklyReportOptedOutAt }).FirstOrDefaultAsync();
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] },] };
                }

                var lastSent = await uow.GetRepository<WeeklyReportLog>().GetManyQueryable(t => t.UserId == userId && t.SentDate != null)
                    .MaxAsync(t => t.SentDate);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Subscribed = user.WeeklyReportOptedOutAt is null,
                        IncludedInPlan = await HasFeatureAsync(userId),
                        Email = user.Email,
                        EmailConfirmed = user.EmailConfirmed,
                        LastSentDate = lastSent,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> SetSubscriptionAsync(long userId, bool subscribed)
        {
            try
            {
                var affected = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ApplicationUser>()
                    .GetManyQueryable(t => t.Id == userId)
                    .ExecuteUpdateAsync(t => t.SetProperty(u => u.WeeklyReportOptedOutAt, subscribed ? null : DateTimeOffset.UtcNow));

                return affected == 0
                    ? new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserNotFound"] },] }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> UnsubscribeAsync(long userId, [NotNull] string token)
        {
            try
            {
                string unprotected;
                try
                {
                    unprotected = dataProtectionProvider.Value.CreateProtector(UnsubscribeProtectorPurpose).Unprotect(token);
                }
                catch (CryptographicException)
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidUnsubscribeToken"] },] };
                }

                return long.TryParse(unprotected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenUserId) && tokenUserId == userId
                    ? await SetSubscriptionAsync(userId, subscribed: false)
                    : new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidUnsubscribeToken"] },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// Users whose current plan includes the weekly report, with a confirmed email, not opted out, and not yet
        /// handled for this week. <paramref name="testRecipients"/>, when set, narrows it to those users (sandbox QA).
        /// </summary>
        private async Task<List<long>> GetCandidatesAsync(DateTimeOffset weekStart, long[] testRecipients, int max)
        {
            var now = DateTimeOffset.UtcNow;
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var planUsers = uow.GetRepository<UserSubscriptionQuotaFeature>().GetManyQueryable(t => t.Feature!.Code == FeatureCodes.WeeklyReport
                    && t.UserSubscriptionQuota!.UserSubscription!.Status == UserSubscriptionStatus.Active
                    && (t.UserSubscriptionQuota.UserSubscription.ExpirationDate == null || t.UserSubscriptionQuota.UserSubscription.ExpirationDate > now))
                .Select(t => t.UserSubscriptionQuota!.UserSubscription!.UserId);
            var handled = uow.GetRepository<WeeklyReportLog>().GetManyQueryable(t => t.WeekStart == weekStart).Select(t => t.UserId);

            var query = uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Enabled && t.Email != null && t.EmailConfirmed
                && t.WeeklyReportOptedOutAt == null && planUsers.Contains(t.Id) && !handled.Contains(t.Id));
            if (testRecipients.Length > 0)
            {
                query = query.Where(t => testRecipients.Contains(t.Id));
            }

            return await query.OrderBy(t => t.Id).Select(t => t.Id).Take(max).ToListAsync();
        }

        /// <summary>
        /// Handles one user's week. The log row is written first: its unique (UserId, WeekStart) index stops a second
        /// run sending the same week. A failed send removes the row again, so the next run retries.
        /// </summary>
        private async Task<WeeklyReportKind> SendOneAsync(long userId, DateTimeOffset weekStart)
        {
            var report = await BuildReportAsync(userId, weekStart);
            var kind = WeeklyReportKind.Report;
            if (!WeeklyReportRules.HasActivity(report))
            {
                kind = WeeklyReportRules.ShouldRemind(report.Plan, await GetLastReminderWeekAsync(userId, weekStart), weekStart)
                    ? WeeklyReportKind.Reminder
                    : WeeklyReportKind.Skipped;
            }

            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var log = new WeeklyReportLog { UserId = userId, WeekStart = weekStart, Kind = kind, CreationDate = DateTimeOffset.UtcNow };
            uow.GetRepository<WeeklyReportLog>().Add(log);
            try
            {
                _ = await uow.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Another run got there first (the unique index): nothing to do for this user.
                return WeeklyReportKind.Skipped;
            }

            if (kind == WeeklyReportKind.Skipped)
            {
                return kind;
            }

            var email = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId).Select(t => t.Email).FirstAsync();
            var links = GetLinks(userId);
            var content = kind == WeeklyReportKind.Report ? WeeklyReportEmailBuilder.Report(report, links) : WeeklyReportEmailBuilder.Reminder(report, links);

            var request = new SendEmailRequestDto
            {
                From = emailService.Value.GetNoReplyEmail(),
                Subject = content.Subject,
                Body = content.Html,
                EmailAddresses = [email!],
            };
            try
            {
                var sent = await emailService.Value.SendEmailAsync(request);
                if (sent.OperationResult != OperationResult.Succeeded)
                {
                    throw new InvalidOperationException($"Weekly report email to user {userId} failed: {string.Join(", ", sent.Errors?.Select(t => t.Message) ?? [])}");
                }
            }
            catch
            {
                // Removed so a later run retries this user.
                _ = await uow.GetRepository<WeeklyReportLog>().GetManyQueryable(t => t.Id == log.Id).ExecuteDeleteAsync();
                throw;
            }

            log.SentDate = DateTimeOffset.UtcNow;
            _ = uow.GetRepository<WeeklyReportLog>().Update(log);
            _ = await uow.SaveChangesAsync();
            return kind;
        }

        private async Task<bool> HasFeatureAsync(long userId)
        {
            var subscription = await subscriptionQuotaService.Value.GetCurrentSubscriptionAsync(userId);
            return subscription.Data?.FeatureGroups?.Any(t => t.Features.Any(f => f.FeatureCode == FeatureCodes.WeeklyReport)) == true;
        }

        private async Task<DateTimeOffset?> GetLastReminderWeekAsync(long userId, DateTimeOffset weekStart) =>
            await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<WeeklyReportLog>()
                .GetManyQueryable(t => t.UserId == userId && t.Kind == WeeklyReportKind.Reminder && t.SentDate != null && t.WeekStart < weekStart)
                .MaxAsync(t => (DateTimeOffset?)t.WeekStart);

        private WeeklyReportEmailBuilder.Links GetLinks(long userId)
        {
            var site = (configuration.Value.GetValue<string>("WeeklyReport:SiteUrl") ?? "https://gamatrain.com").TrimEnd('/');
            var token = dataProtectionProvider.Value.CreateProtector(UnsubscribeProtectorPurpose).Protect(userId.ToString(CultureInfo.InvariantCulture));
            var unsubscribeBase = configuration.Value.GetValue<string>("Notifications:UnsubscribeBaseUrl") ?? $"{site}/unsubscribe";
            return new(site, $"{unsubscribeBase}?type=weekly-report&userId={userId}&token={Uri.EscapeDataString(token)}");
        }

        /// <summary>Everything the report shows for one user and week. Read-only.</summary>
        private async Task<WeeklyReportDto> BuildReportAsync(long userId, DateTimeOffset weekStart)
        {
            var weekEnd = weekStart.AddDays(7);
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId)
                .Select(t => new { t.FirstName, t.SchoolId, IsTeacher = t.UserRoles!.Any(r => r.Role!.Name == nameof(Role.Teacher)) })
                .FirstAsync();

            var answers = await uow.GetRepository<TestSubmission>().GetManyQueryable(t => t.UserId == userId && t.CreationDate >= weekStart && t.CreationDate < weekEnd)
                .GroupBy(t => 1).Select(t => new { Count = t.Count(), Correct = t.Count(x => x.IsCorrect) }).FirstOrDefaultAsync();
            var practiceDays = await uow.GetRepository<TestSubmission>().GetManyQueryable(t => t.UserId == userId && t.CreationDate < weekEnd)
                .Select(t => t.CreationDate.UtcDateTime.Date).Distinct().ToListAsync();
            var streak = ProfileProgressRules.Streak(practiceDays.Select(DateOnly.FromDateTime), DateOnly.FromDateTime(weekEnd.AddDays(-1).UtcDateTime));

            var pointsEarned = await uow.GetRepository<Transaction>().GetManyQueryable(t => t.UserId == userId && !t.IsDebit && t.CreationDate >= weekStart && t.CreationDate < weekEnd)
                .SumAsync(t => (long?)t.Points) ?? 0;

            // A charge logs +Amount and its refund -Amount; content downloads charge their price, so count events, not amounts.
            var usage = await uow.GetRepository<SubscriptionQuotaConsumptionLog>().GetManyQueryable(t => t.UserId == userId
                    && t.CreationDate >= weekStart && t.CreationDate < weekEnd && t.Feature!.Code != FeatureCodes.WeeklyReport)
                .GroupBy(t => t.Feature!.Name)
                .Select(t => new { Name = t.Key, Count = t.Count(x => x.Amount > 0) - t.Count(x => x.Amount < 0) })
                .ToListAsync();

            var contributions = await uow.GetRepository<Contribution>().GetManyQueryable(t => t.CreationUserId == userId && t.CreationDate >= weekStart && t.CreationDate < weekEnd
                    && t.Status != Status.Deleted)
                .GroupBy(t => new { t.CategoryType, t.Status }).Select(t => new { t.Key.CategoryType, t.Key.Status, Count = t.Count() })
                .ToListAsync();

            var school = await GetSchoolAsync(uow, userId, user.SchoolId, weekStart, weekEnd);
            var reportsSoFar = await uow.GetRepository<WeeklyReportLog>().CountAsync(t => t.UserId == userId && t.Kind == WeeklyReportKind.Report && t.SentDate != null);

            return new()
            {
                UserId = userId,
                FirstName = user.FirstName,
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                IsTeacher = user.IsTeacher,
                QuestionsAnswered = answers?.Count ?? 0,
                QuestionsCorrect = answers?.Correct ?? 0,
                Streak = streak.Current,
                PointsEarned = pointsEarned,
                Plan = await GetPlanAsync(userId),
                UsageThisWeek = [.. usage.Where(t => t.Count > 0).OrderByDescending(t => t.Count).Select(t => new WeeklyReportCountDto { Name = t.Name ?? "Other", Count = t.Count })],

                // Draft and in review read the same to the user: waiting for a check.
                Contributions = [.. contributions
                    .GroupBy(t => new { t.CategoryType, Status = t.Status == Status.Draft ? Status.Review : t.Status })
                    .Select(t => new WeeklyReportContributionDto { Category = t.Key.CategoryType, Status = t.Key.Status, Count = t.Sum(x => x.Count) })
                    .OrderBy(t => t.Status == Status.Confirmed ? 0 : 1).ThenByDescending(t => t.Count)],
                School = school,
                ShowAddSchool = school is null && reportsSoFar < WeeklyReportRules.AddSchoolReports,
                Teacher = user.IsTeacher ? await GetTeacherAsync(uow, userId, weekStart, weekEnd) : null,
            };
        }

        /// <summary>The current plan's quota buckets, without the weekly report's own entry (it has no quota to show).</summary>
        private async Task<WeeklyReportPlanDto?> GetPlanAsync(long userId)
        {
            var subscription = (await subscriptionQuotaService.Value.GetCurrentSubscriptionAsync(userId)).Data;
            return subscription is null ? null : new()
            {
                Title = subscription.PlanTitle,
                PeriodEnd = subscription.ExpirationDate,
                Renews = subscription.AutoRenews && !subscription.CancelAtPeriodEnd,
                Quotas = [.. (subscription.FeatureGroups ?? [])
                    .Where(t => t.Features.Any(f => f.FeatureCode != FeatureCodes.WeeklyReport))
                    .Select(t => new WeeklyReportQuotaDto
                    {
                        Name = string.Join(", ", t.Features.Where(f => f.FeatureCode != FeatureCodes.WeeklyReport).Select(f => f.FeatureName ?? f.FeatureCode)),
                        Used = t.Used,
                        Limit = t.Limit,
                    })],
            };
        }

        /// <summary>The profile school, else the most recently joined one; a hidden school counts as none. Public rating only.</summary>
        private static async Task<WeeklyReportSchoolDto?> GetSchoolAsync(IUnitOfWork uow, long userId, long? profileSchoolId, DateTimeOffset weekStart, DateTimeOffset weekEnd)
        {
            var schoolId = profileSchoolId ?? await uow.GetRepository<SchoolMember>().GetManyQueryable(t => t.UserId == userId)
                .OrderByDescending(t => t.CreationDate).Select(t => (long?)t.SchoolId).FirstOrDefaultAsync();
            if (schoolId is null)
            {
                return null;
            }

            var school = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolId && !t.IsDeleted)
                .Select(t => new { t.Id, t.Name, t.CityId, CityName = t.City!.Title, t.CommentsRatingSum, t.CommentsRatingCount })
                .FirstOrDefaultAsync();
            if (school is null)
            {
                return null;
            }

            double? cityAverage = null;
            if (school.CityId is not null)
            {
                var rated = await uow.GetRepository<School>().GetManyQueryable(t => t.CityId == school.CityId && !t.IsDeleted && t.CommentsRatingCount > 0)
                    .GroupBy(t => 1).Select(t => new { Count = t.Count(), Average = t.Average(x => x.CommentsRatingSum!.Value / x.CommentsRatingCount!.Value) })
                    .FirstOrDefaultAsync();
                cityAverage = rated is { Count: >= MinRatedSchoolsForCityAverage } ? rated.Average : null;
            }

            return new()
            {
                Id = school.Id,
                Name = school.Name,
                Rating = school.CommentsRatingCount > 0 ? school.CommentsRatingSum / school.CommentsRatingCount : null,
                Reviews = school.CommentsRatingCount ?? 0,
                CityName = school.CityName,
                CityAverage = cityAverage,
                NewReviews = await uow.GetRepository<SchoolComment>().CountAsync(t => t.SchoolId == school.Id && t.CreationDate >= weekStart && t.CreationDate < weekEnd),
                NewPhotos = await uow.GetRepository<SchoolImage>().CountAsync(t => t.SchoolId == school.Id && t.CreationDate >= weekStart && t.CreationDate < weekEnd),
                NewMembers = await uow.GetRepository<SchoolMember>().CountAsync(t => t.SchoolId == school.Id && t.CreationDate >= weekStart && t.CreationDate < weekEnd),
            };
        }

        private static async Task<WeeklyReportTeacherDto> GetTeacherAsync(IUnitOfWork uow, long userId, DateTimeOffset weekStart, DateTimeOffset weekEnd) => new()
        {
            PaperDownloads = await uow.GetRepository<ContentOwnerCommission>().CountAsync(t => t.OwnerUserId == userId && t.Reason == CommissionReason.ContentDownload
                && t.CreationDate >= weekStart && t.CreationDate < weekEnd),
            JoinedByInvite = await uow.GetRepository<ApplicationUser>().CountAsync(t => t.ReferredByUserId == userId && t.ReferredDate >= weekStart && t.ReferredDate < weekEnd),
            PostsPublished = await uow.GetRepository<Post>().CountAsync(t => t.CreationUserId == userId && t.Status == Status.Confirmed
                && t.CreationDate >= weekStart && t.CreationDate < weekEnd),
        };
    }
}
