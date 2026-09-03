namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Data.Dto.Nudge;
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
    /// The proactive/scheduled nudge system - see docs/business/notifications.md, "Nudge system". Adding a new
    /// NudgeType means: add the enum value, add its condition to ApplyEligibilityFilter below, add it to
    /// AllNudgeTypes, and create its NudgeTemplate row (admin UI, no deploy needed for that part).
    /// </summary>
    public sealed class NudgeService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<NudgeService>> localizer, Lazy<ILogger<NudgeService>> logger, Lazy<IEmailService> emailService
        , Lazy<IDataProtectionProvider> dataProtectionProvider, Lazy<IConfiguration> configuration)
        : LocalizableServiceBase<NudgeService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), INudgeService
    {
        /// <summary>
        /// IDataProtectionProvider purpose string for the unsubscribe token - deliberately not Identity's
        /// UserManager token provider (ApiDataProtectorTokenProviderOptions.TokenLifespan is a single
        /// provider-wide 10-day-default setting shared by every purpose that provider mints for, wrong for a
        /// link that must still work whenever an unread email finally gets opened, weeks later). This token has
        /// no expiration by design - Protect/Unprotect with no ITimeLimitedDataProtector involved.
        /// </summary>
        private const string UnsubscribeProtectorPurpose = "NudgeUnsubscribe";


        /// <summary>A user is never evaluated for any nudge until they've been registered at least this long.</summary>
        private const int MinDaysSinceRegistration = 7;

        /// <summary>
        /// The floor: any two nudges to the same user - whatever their NudgeTypes - must be at least this many
        /// days apart. Added 2026-09-02 after a real spam complaint: a user eligible for several NudgeTypes at
        /// once (a long-registered, never-completed profile) was getting one email per type, all the same
        /// night. This is checked globally, across all types, not per type - see EvaluateAndSendNudgesAsync.
        /// </summary>
        private const int MinDaysBetweenAnyNudge = 7;

        /// <summary>Minimum gap between resends of the *same* NudgeType to the same user - stricter than, and only relevant once, MinDaysBetweenAnyNudge above has already passed.</summary>
        private const int ResendCooldownDays = 14;

        /// <summary>A user is never sent the same NudgeType more than this many times, ever.</summary>
        private const int MaxSendCount = 3;

        /// <summary>
        /// Hard cap on total nudge emails actually sent in one run, across every NudgeType combined. Added
        /// 2026-09-02: with ~30k existing production users, a first run (or any run with a large backlog - e.g.
        /// right after this feature first deploys, when everyone who's ever been eligible becomes a candidate
        /// at once) could otherwise try to send thousands of emails in a single job execution. Reuses the same
        /// number ResendEmailProvider already chunks recipient lists at (Chunk(100)) - Resend's own per-call
        /// limit - for consistency, though this cap is enforced differently: each nudge send is already its own
        /// single-recipient call, so this bounds the *count* of sequential calls this run makes, not a batch
        /// size. Once hit, the run stops sending entirely (regardless of which NudgeType or how many candidates
        /// remain) - anyone not reached this run is picked up on a later one, since none of their state
        /// (UserNudgeLog, AllNudgesCompletedAt) changes until they're actually sent to.
        /// </summary>
        private const int MaxSendsPerRun = 100;

        private static readonly NudgeType[] AllNudgeTypes =
        [
            NudgeType.RoleMissing, NudgeType.AvatarMissing, NudgeType.NameMissing,
            NudgeType.BioMissing, NudgeType.SkillsMissing, NudgeType.ExperienceMissing,
        ];

        public async Task<ResultData<ListDataSource<NudgeTemplateDto>>> GetNudgeTemplatesAsync(ListRequestDto<NudgeTemplate>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<NudgeTemplate, int>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new NudgeTemplateDto
                {
                    Id = t.Id,
                    NudgeType = t.NudgeType,
                    Subject = t.Subject,
                    Body = t.Body,
                    CtaLabel = t.CtaLabel,
                    CtaUrl = t.CtaUrl,
                    IsActive = t.IsActive,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<NudgeTemplateDto>> GetNudgeTemplateAsync([NotNull] ISpecification<NudgeTemplate> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var template = await uow.GetRepository<NudgeTemplate, int>().GetAsync(specification);
                return template is null
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["NudgeTemplateNotFound"] },] }
                    : new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Id = template.Id,
                            NudgeType = template.NudgeType,
                            Subject = template.Subject,
                            Body = template.Body,
                            CtaLabel = template.CtaLabel,
                            CtaUrl = template.CtaUrl,
                            IsActive = template.IsActive,
                        },
                    };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<int>> ManageNudgeTemplateAsync([NotNull] ManageNudgeTemplateRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<NudgeTemplate, int>();
                NudgeTemplate? template;

                if (requestDto.Id.HasValue)
                {
                    template = await repository.GetAsync(requestDto.Id.Value);
                    if (template is null)
                    {
                        return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["NudgeTemplateNotFound"] },] };
                    }

                    template.NudgeType = requestDto.NudgeType ?? template.NudgeType;
                    template.Subject = requestDto.Subject ?? template.Subject;
                    template.Body = requestDto.Body ?? template.Body;
                    template.CtaLabel = requestDto.CtaLabel ?? template.CtaLabel;
                    template.CtaUrl = requestDto.CtaUrl ?? template.CtaUrl;
                    template.IsActive = requestDto.IsActive ?? template.IsActive;
                    _ = repository.Update(template);
                }
                else
                {
                    template = new NudgeTemplate
                    {
                        NudgeType = requestDto.NudgeType,
                        Subject = requestDto.Subject,
                        Body = requestDto.Body,
                        CtaLabel = requestDto.CtaLabel,
                        CtaUrl = requestDto.CtaUrl,
                        IsActive = requestDto.IsActive.GetValueOrDefault(),
                        CreationDate = DateTimeOffset.UtcNow,
                    };
                    repository.Add(template);
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = template.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RemoveNudgeTemplateAsync([NotNull] ISpecification<NudgeTemplate> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<NudgeTemplate, int>();
                var template = await repository.GetAsync(specification);
                if (template is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["NudgeTemplateNotFound"] },] };
                }

                repository.Remove(template);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> EvaluateAndSendNudgesAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var registrationCutoff = DateTimeOffset.UtcNow.AddDays(-MinDaysSinceRegistration);
                var sameTypeCooldownCutoff = DateTimeOffset.UtcNow.AddDays(-ResendCooldownDays);
                var anyNudgeCutoff = DateTimeOffset.UtcNow.AddDays(-MinDaysBetweenAnyNudge);

                // Candidate pool: registered long enough, has an email, and not yet marked fully complete.
                // ApplicationUser.AllNudgesCompletedAt is what keeps this job cheap as the user base grows - a
                // user already known to have zero remaining gaps is excluded here, via one indexed null-check,
                // before any of the six per-field text-column scans below ever run against them again. See that
                // property's doc comment and docs/business/notifications.md.
                var candidateUsers = await uow.GetRepository<ApplicationUser>()
                    .GetManyQueryable(t => t.RegistrationDate != null && t.RegistrationDate <= registrationCutoff && t.Email != null
                        && t.AllNudgesCompletedAt == null && t.NudgesOptedOutAt == null)
                    .Select(t => new { t.Id, t.FirstName, t.LastName, t.Email })
                    .ToListAsync();
                if (candidateUsers.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var candidateUserIds = candidateUsers.Select(t => t.Id).ToList();

                // Bug fixed 2026-09-02, found live in sandbox: a user eligible for several NudgeTypes at once
                // (a long-registered account that never completed any profile field - exactly the oldest
                // accounts, since they've had the most time to accumulate missing fields without ever filling
                // one in) got a separate email per type, all in the same run - reads as spam. Fixed with a
                // GLOBAL cooldown, not just the per-type one below: any two nudges to the same user, whatever
                // their types, must be at least MinDaysBetweenAnyNudge apart. Seeded from UserNudgeLog's most
                // recent send per user (any NudgeType) and then added to live as sends happen in this same run
                // - so it also naturally caps this run to at most one send per user (0 days apart would
                // violate the same rule), with no separate "already sent this run" mechanism needed. Whichever
                // NudgeType a user doesn't get nudged for today waits for a later run - still subject to its
                // own per-type cooldown/cap once it does fire.
                var recentlyNudgedUserIds = await uow.GetRepository<UserNudgeLog>()
                    .GetManyQueryable(t => candidateUserIds.Contains(t.UserId) && t.LastSentDate > anyNudgeCutoff)
                    .Select(t => t.UserId)
                    .ToListAsync();
                HashSet<long> excludedUserIds = [.. recentlyNudgedUserIds];

                // Tracks who's still missing at least one nudge-relevant field this run, independent of cooldown
                // state - completeness is a different question from "gets emailed today", see below.
                HashSet<long> stillIncompleteUserIds = [];
                var totalSentThisRun = 0;

                foreach (var nudgeType in AllNudgeTypes)
                {
                    var template = await uow.GetRepository<NudgeTemplate, int>().GetManyQueryable(t => t.NudgeType == nudgeType && t.IsActive).FirstOrDefaultAsync();
                    if (template is null)
                    {
                        continue;
                    }

                    // Everyone in the candidate pool still missing this field, regardless of cooldown state or
                    // MaxSendsPerRun below - this feeds stillIncompleteUserIds, separately from who actually
                    // gets emailed. Deliberately NOT skipped once the send cap is hit (see below): skipping it
                    // would wrongly let a user whose only remaining gap is a NudgeType this run never got to
                    // get latched as AllNudgesCompletedAt anyway.
                    var missingUserIds = await ApplyEligibilityFilter(
                            uow.GetRepository<ApplicationUser>().GetManyQueryable(t => candidateUserIds.Contains(t.Id)),
                            nudgeType)
                        .Select(t => t.Id)
                        .ToListAsync();
                    stillIncompleteUserIds.UnionWith(missingUserIds);

                    // MaxSendsPerRun only gates sending, never the completeness tracking above - once hit, later
                    // NudgeTypes are still scanned (cheaply, against the already-small candidate pool) purely to
                    // keep stillIncompleteUserIds accurate, they just never reach the point of emailing anyone.
                    if (totalSentThisRun >= MaxSendsPerRun)
                    {
                        continue;
                    }

                    // Users already at the send cap for this specific NudgeType - excluded up front rather than
                    // joined, since UserNudgeLog has no navigation from ApplicationUser. The same-type cooldown
                    // (sameTypeCooldownCutoff) only matters once MinDaysBetweenAnyNudge has already passed - it
                    // stays a stricter, longer wait before literally the same nudge repeats.
                    var atSendCapUserIds = await uow.GetRepository<UserNudgeLog>()
                        .GetManyQueryable(t => t.NudgeType == nudgeType && (t.SendCount >= MaxSendCount || t.LastSentDate > sameTypeCooldownCutoff))
                        .Select(t => t.UserId)
                        .ToListAsync();

                    var usersToNudge = candidateUsers.Where(t => missingUserIds.Contains(t.Id) && !atSendCapUserIds.Contains(t.Id) && !excludedUserIds.Contains(t.Id));

                    foreach (var user in usersToNudge)
                    {
                        if (totalSentThisRun >= MaxSendsPerRun)
                        {
                            break;
                        }

                        if (!excludedUserIds.Add(user.Id))
                        {
                            continue;
                        }

                        var body = template.Body?
                            .Replace("[RECEIVER_NAME]", $"{user.FirstName} {user.LastName}".Trim(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[CTA_URL]", template.CtaUrl, StringComparison.OrdinalIgnoreCase)
                            + BuildUnsubscribeFooter(user.Id);

                        _ = await emailService.Value.SendEmailAsync(new SendEmailRequestDto
                        {
                            From = emailService.Value.GetNoReplyEmail(),
                            Subject = template.Subject!,
                            Body = body!,
                            EmailAddresses = [user.Email!],
                        });

                        await UpsertNudgeLogAsync(uow, user.Id, nudgeType);
                        totalSentThisRun++;
                    }
                }

                // Anyone in the candidate pool who never showed up as still-missing for any type this run has
                // zero remaining nudge-relevant gaps - latch AllNudgesCompletedAt so future runs never
                // re-scan them (see that property's doc comment for the accepted one-way tradeoff).
                var newlyCompleteUserIds = candidateUserIds.Except(stillIncompleteUserIds).ToList();
                if (newlyCompleteUserIds.Count > 0)
                {
                    _ = await uow.GetRepository<ApplicationUser>()
                        .GetManyQueryable(t => newlyCompleteUserIds.Contains(t.Id))
                        .ExecuteUpdateAsync(t => t.SetProperty(u => u.AllNudgesCompletedAt, DateTimeOffset.UtcNow));
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        private static async Task UpsertNudgeLogAsync(IUnitOfWork uow, long userId, NudgeType nudgeType)
        {
            var repository = uow.GetRepository<UserNudgeLog>();
            var log = await repository.GetManyQueryable(t => t.UserId == userId && t.NudgeType == nudgeType).FirstOrDefaultAsync();
            if (log is null)
            {
                repository.Add(new UserNudgeLog { UserId = userId, NudgeType = nudgeType, LastSentDate = DateTimeOffset.UtcNow, SendCount = 1 });
            }
            else
            {
                log.LastSentDate = DateTimeOffset.UtcNow;
                log.SendCount++;
                _ = repository.Update(log);
            }

            _ = await uow.SaveChangesAsync();
        }

        /// <summary>Appended to every nudge email, regardless of what the admin wrote in the template - never relies on a placeholder that could be left out. See UnsubscribeAsync.</summary>
        private string BuildUnsubscribeFooter(long userId)
        {
            var protector = dataProtectionProvider.Value.CreateProtector(UnsubscribeProtectorPurpose);
            var token = protector.Protect(userId.ToString(CultureInfo.InvariantCulture));
            var baseUrl = configuration.Value.GetValue<string>("Notifications:UnsubscribeBaseUrl");
            var url = $"{baseUrl}?userId={userId}&token={Uri.EscapeDataString(token)}";
            return $"<br><br><a href=\"{url}\">Unsubscribe from these emails</a>";
        }

        public async Task<ResultData<bool>> UnsubscribeAsync(long userId, [NotNull] string token)
        {
            try
            {
                var protector = dataProtectionProvider.Value.CreateProtector(UnsubscribeProtectorPurpose);
                string unprotected;
                try
                {
                    unprotected = protector.Unprotect(token);
                }
                catch (CryptographicException)
                {
                    // Wrong/tampered/foreign token - a normal, expected outcome for a public, unauthenticated
                    // endpoint, not an infrastructure failure. Logged at Failed severity would be noise.
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidUnsubscribeToken"] },] };
                }

                var tokenMatchesUser = long.TryParse(unprotected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenUserId) && tokenUserId == userId;
                return tokenMatchesUser
                    ? await SetNudgeSubscriptionAsync(userId, subscribed: false)
                    : new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidUnsubscribeToken"] },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> SetNudgeSubscriptionAsync(long userId, bool subscribed)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();
                var user = await repository.GetAsync(userId);
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserNotFound"] },] };
                }

                user.NudgesOptedOutAt = subscribed ? null : DateTimeOffset.UtcNow;
                _ = repository.Update(user);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// One condition per NudgeType - the part that genuinely can't be data-driven, since each nudge's
        /// trigger is a real, different query. Thresholds (bio length, etc.) deliberately match
        /// UserRateLevel.Calculate / IdentityService.BuildDashboardProfileCompletionAsync, so a field counted
        /// "complete" on the dashboard is never still nudged for here, and vice versa.
        /// </summary>
        private static IQueryable<ApplicationUser> ApplyEligibilityFilter(IQueryable<ApplicationUser> query, NudgeType nudgeType) => nudgeType switch
        {
            _ when nudgeType == NudgeType.RoleMissing => query.Where(t => t.Group == null),
            _ when nudgeType == NudgeType.AvatarMissing => query.Where(t => string.IsNullOrEmpty(t.AvatarId)),
            _ when nudgeType == NudgeType.NameMissing => query.Where(t => string.IsNullOrEmpty(t.FirstName) || string.IsNullOrEmpty(t.LastName)),
            _ when nudgeType == NudgeType.BioMissing => query.Where(t => string.IsNullOrEmpty(t.Biography) || t.Biography.Length <= 49),
            _ when nudgeType == NudgeType.SkillsMissing => query.Where(t => string.IsNullOrEmpty(t.Skills)),
            _ when nudgeType == NudgeType.ExperienceMissing => query.Where(t => t.Experiences == null || !t.Experiences.Any()),
            _ => query.Where(_ => false),
        };
    }
}
