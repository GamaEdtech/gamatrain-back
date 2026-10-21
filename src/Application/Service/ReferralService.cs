namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Referral;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>Teacher invite program. See IReferralService and docs/business/referrals.md.</summary>
    public class ReferralService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<ReferralService>> localizer, Lazy<ILogger<ReferralService>> logger
        , Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<IEmailService> emailService, Lazy<IConfiguration> configuration)
        : LocalizableServiceBase<ReferralService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IReferralService
    {
        private const int ReferralCodeMaxLength = 10;
        private const int AccrualBatchSize = 500;
        private const int OfferEmailBatchSize = 100;
        private static readonly TimeSpan PendingReferralLifetime = TimeSpan.FromDays(1);
        private static readonly string TeacherRoleName = nameof(Role.Teacher).ToUpperInvariant();

        public async Task<ResultData<bool>> CreateOfferAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (!await IsTeacherAsync(uow, userId)
                    || await uow.GetRepository<ReferralMembership>().AnyAsync(t => t.UserId == userId))
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var settings = await GetSettingsAsync();
                var percent = ReferralTerms.Percent(settings);
                if (percent <= 0)
                {
                    // An admin set the program to 0%: nothing worth offering.
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var now = DateTimeOffset.UtcNow;
                uow.GetRepository<ReferralMembership>().Add(new()
                {
                    UserId = userId,
                    OfferPercent = percent,
                    OfferDate = now,
                    OfferExpirationDate = ReferralTerms.OfferExpiration(settings, now),
                });
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (UniqueConstraintException)
            {
                // Another sign-in created it at the same moment.
                return new(OperationResult.Succeeded) { Data = false };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ReferralStatusDto>> GetStatusAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                return new(OperationResult.Succeeded) { Data = await BuildStatusAsync(uow, userId) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ReferralStatusDto>> JoinAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (!await IsTeacherAsync(uow, userId))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Only teachers can join the invite program." }] };
                }

                var now = DateTimeOffset.UtcNow;
                var settings = await GetSettingsAsync();
                var currentPercent = ReferralTerms.Percent(settings);
                var memberships = uow.GetRepository<ReferralMembership>();
                var membership = await memberships.GetManyQueryable(t => t.UserId == userId)
                    .Select(t => new { t.Id, t.OfferPercent, t.OfferExpirationDate, t.JoinedDate })
                    .FirstOrDefaultAsync();

                if (membership is null)
                {
                    // A teacher from before the program (no offer email): joins at today's percent.
                    if (currentPercent <= 0)
                    {
                        return ProgramClosed();
                    }

                    try
                    {
                        memberships.Add(new()
                        {
                            UserId = userId,
                            OfferPercent = currentPercent,
                            OfferDate = now,
                            OfferEmailSentDate = now,
                            JoinedDate = now,
                            Percent = currentPercent,
                        });
                        _ = await uow.SaveChangesAsync();
                    }
                    catch (UniqueConstraintException)
                    {
                        // A second click at the same moment: the first one's row stands.
                    }
                }
                else if (membership.JoinedDate is null)
                {
                    var percent = ReferralTerms.PercentOnJoin(membership.OfferPercent, membership.OfferExpirationDate, currentPercent, now);
                    if (percent <= 0)
                    {
                        return ProgramClosed();
                    }

                    // Guarded on JoinedDate == null: a double click can't re-lock a different percent.
                    _ = await memberships.GetManyQueryable(t => t.Id == membership.Id && t.JoinedDate == null).ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.JoinedDate, now)
                        .SetProperty(p => p.Percent, percent));
                }

                await EnsureReferralCodeAsync(userId);

                return new(OperationResult.Succeeded) { Data = await BuildStatusAsync(UnitOfWorkProvider.Value.CreateUnitOfWork(), userId) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }

            static ResultData<ReferralStatusDto> ProgramClosed() =>
                new(OperationResult.NotValid) { Errors = [new() { Message = "The invite program isn't open right now." }] };
        }

        public async Task<ResultData<ListDataSource<InvitedUserDto>>> GetInvitedUsersAsync(long userId, PagingDto? pagingDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var commissions = uow.GetRepository<ContentOwnerCommission>().GetManyQueryable();
                var subscriptions = uow.GetRepository<UserSubscription>().GetManyQueryable();

                // Newest first; a client sort is dropped (FilterListAsync would replace this order).
                var result = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.ReferredByUserId == userId)
                    .OrderByDescending(t => t.ReferredDate).ThenByDescending(t => t.Id)
                    .FilterListAsync(pagingDto is null ? null : new() { PageFilter = pagingDto.PageFilter });

                var rows = await result.List.Select(t => new
                {
                    t.FirstName,
                    t.LastName,
                    t.ReferredDate,
                    Subscribed = subscriptions.Any(s => s.UserId == t.Id && s.Status == UserSubscriptionStatus.Active),
                    PaymentsCount = commissions.Count(c => c.OwnerUserId == userId && c.DownloaderUserId == t.Id && c.Reason == CommissionReason.ReferralSubscription),
                    EarnedUsd = commissions.Where(c => c.OwnerUserId == userId && c.DownloaderUserId == t.Id && c.Reason == CommissionReason.ReferralSubscription)
                        .Sum(c => (decimal?)c.AmountUsd) ?? 0m,
                }).ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        List = rows.Select(t => new InvitedUserDto
                        {
                            DisplayName = ShortName(t.FirstName, t.LastName),
                            JoinedDate = t.ReferredDate,
                            Subscribed = t.Subscribed,
                            PaymentsCount = t.PaymentsCount,
                            EarnedUsd = t.EarnedUsd,
                        }).ToList(),
                        TotalRecordsCount = result.TotalRecordsCount,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> BindInviterAsync(long userId, string? referralCode)
        {
            try
            {
                var code = NormalizeCode(referralCode);
                if (code is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var inviterId = await FindJoinedInviterAsync(uow, code);
                if (inviterId is null || inviterId == userId)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                // Guarded on ReferredByUserId == null: an inviter, once set, never changes.
                var affected = await uow.GetRepository<ApplicationUser>()
                    .GetManyQueryable(t => t.Id == userId && t.ReferredByUserId == null)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.ReferredByUserId, inviterId)
                        .SetProperty(p => p.ReferredDate, DateTimeOffset.UtcNow));
                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                // Never fails the sign-up that called it.
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> SavePendingReferralAsync([NotNull] string identity, string? referralCode)
        {
            try
            {
                var code = NormalizeCode(referralCode);
                var normalizedIdentity = NormalizeIdentity(identity);
                if (code is null || normalizedIdentity is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await FindJoinedInviterAsync(uow, code) is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var now = DateTimeOffset.UtcNow;
                var repository = uow.GetRepository<PendingReferral>();

                // Expired rows go here, so no cleanup job is needed; the same identity's older code is replaced.
                _ = await repository.GetManyQueryable(t => t.ExpirationDate < now || t.Identity == normalizedIdentity).ExecuteDeleteAsync();
                repository.Add(new()
                {
                    Identity = normalizedIdentity,
                    ReferralCode = code,
                    CreationDate = now,
                    ExpirationDate = now.Add(PendingReferralLifetime),
                });
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (UniqueConstraintException)
            {
                // The same register step twice at once: one row is enough.
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> BindPendingReferralAsync(long userId, [NotNull] IEnumerable<string?> identities)
        {
            try
            {
                var normalized = identities.Select(NormalizeIdentity).OfType<string>().Distinct().ToList();
                if (normalized.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PendingReferral>();
                var now = DateTimeOffset.UtcNow;
                var code = await repository.GetManyQueryable(t => normalized.Contains(t.Identity!) && t.ExpirationDate >= now)
                    .OrderByDescending(t => t.CreationDate)
                    .Select(t => t.ReferralCode)
                    .FirstOrDefaultAsync();
                if (code is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var bound = await BindInviterAsync(userId, code);
                _ = await repository.GetManyQueryable(t => normalized.Contains(t.Identity!)).ExecuteDeleteAsync();
                return bound;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<int>> AccrueSubscriptionCommissionsAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var commissions = uow.GetRepository<ContentOwnerCommission>().GetManyQueryable();

                // Every paid subscription payment of an invited user, made after they were invited, with no share yet.
                // The unique PaymentId index is the real guard; this filter only keeps the batch small.
                var candidates = await uow.GetRepository<Payment>().GetManyQueryable(t => t.Status == PaymentStatus.Paid
                        && (t.Kind == PaymentKind.NewSubscription || t.Kind == PaymentKind.Renewal || t.Kind == PaymentKind.PlanSwitch)
                        && t.User!.ReferredByUserId != null
                        && t.CreationDate >= t.User.ReferredDate)
                    .Where(t => !commissions.Any(c => c.PaymentId == t.Id))
                    .OrderBy(t => t.Id)
                    .Take(AccrualBatchSize)
                    .Select(t => new { t.Id, t.UserId, t.Amount, t.Currency, t.BaseCurrencyAmount, InviterId = t.User!.ReferredByUserId!.Value })
                    .ToListAsync();
                if (candidates.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = 0 };
                }

                var inviterIds = candidates.Select(t => t.InviterId).Distinct().ToList();
                var percents = await uow.GetRepository<ReferralMembership>()
                    .GetManyQueryable(t => inviterIds.Contains(t.UserId) && t.JoinedDate != null && t.Percent != null)
                    .ToDictionaryAsync(t => t.UserId, t => t.Percent!.Value);

                var added = 0;
                foreach (var payment in candidates)
                {
                    if (!percents.TryGetValue(payment.InviterId, out var percent) || percent <= 0)
                    {
                        continue;
                    }

                    var usd = ReferralTerms.PaymentUsd(payment.Currency.Name, payment.Amount, payment.BaseCurrencyAmount);
                    if (usd is null)
                    {
                        Logger.Value.LogWarning("Referral commission skipped for payment {PaymentId}: no USD value for {Currency}", payment.Id, payment.Currency.Name);
                        continue;
                    }

                    if (usd <= 0)
                    {
                        // A free downgrade switch or a $0 invoice: nothing to share.
                        continue;
                    }

                    try
                    {
                        // One unit of work per row: a duplicate (another run got there first) must not block the rest.
                        var rowUow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                        rowUow.GetRepository<ContentOwnerCommission>().Add(new()
                        {
                            OwnerUserId = payment.InviterId,
                            DownloaderUserId = payment.UserId,
                            Reason = CommissionReason.ReferralSubscription,
                            PaymentId = payment.Id,
                            Points = 0,
                            CommissionPercent = percent,
                            AmountUsd = ReferralTerms.Share(usd.Value, percent),
                            CreationDate = DateTimeOffset.UtcNow,
                        });
                        _ = await rowUow.SaveChangesAsync();
                        added++;
                    }
                    catch (UniqueConstraintException)
                    {
                        // Already accrued.
                    }
                }

                return new(OperationResult.Succeeded) { Data = added };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = 0, Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<int>> SendPendingOfferEmailsAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var memberships = uow.GetRepository<ReferralMembership>();
                var pending = await memberships.GetManyQueryable(t => t.OfferEmailSentDate == null)
                    .OrderBy(t => t.Id)
                    .Take(OfferEmailBatchSize)
                    .Select(t => new { t.Id, t.OfferPercent, t.OfferExpirationDate, t.JoinedDate, t.User!.Email, t.User.FirstName, t.User.LastName })
                    .ToListAsync();
                if (pending.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = 0 };
                }

                var settings = await GetSettingsAsync();
                var template = settings.ReferralOfferEmailTemplate;
                var subject = string.IsNullOrWhiteSpace(settings.ReferralOfferEmailSubject) ? "Teachers are the heart of the Gamatrain community" : settings.ReferralOfferEmailSubject;
                var joinLink = configuration.Value.GetValue<string>("Referral:DashboardUrl");
                if (string.IsNullOrEmpty(joinLink))
                {
                    // Without the dashboard link the email can't say where to join; the offers wait until it is configured.
                    Logger.Value.LogWarning("Referral offer emails not sent: Referral:DashboardUrl is not configured");
                    return new(OperationResult.Succeeded) { Data = 0 };
                }
                var sent = 0;
                foreach (var offer in pending)
                {
                    // Claimed before sending, so two overlapping runs can't email the same teacher twice.
                    var claimed = await memberships.GetManyQueryable(t => t.Id == offer.Id && t.OfferEmailSentDate == null)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.OfferEmailSentDate, DateTimeOffset.UtcNow));
                    if (claimed == 0 || offer.JoinedDate is not null || string.IsNullOrEmpty(offer.Email) || string.IsNullOrEmpty(template))
                    {
                        // Nothing to send: already joined, no address (a phone-only account), or no template.
                        continue;
                    }

                    var name = $"{offer.FirstName} {offer.LastName}".Trim();
                    var firstName = string.IsNullOrWhiteSpace(offer.FirstName) ? "there" : offer.FirstName.Trim();
                    var percent = FormatPercent(offer.OfferPercent);
                    var body = template
                        .Replace("[FIRST_NAME]", WebUtility.HtmlEncode(firstName), StringComparison.OrdinalIgnoreCase)
                        .Replace("[RECEIVER_NAME]", WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "Dear Teacher" : name), StringComparison.OrdinalIgnoreCase)
                        .Replace("[PERCENT]", percent, StringComparison.OrdinalIgnoreCase)
                        .Replace("[JOIN_LINK]", joinLink, StringComparison.OrdinalIgnoreCase)
                        .Replace("[EXPIRY_DATE]", offer.OfferExpirationDate?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? "no end date", StringComparison.OrdinalIgnoreCase);

                    var result = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = subject,
                        Body = body,
                        EmailAddresses = [offer.Email],
                        From = emailService.Value.GetNoReplyEmail(),
                    });
                    if (result.OperationResult is OperationResult.Succeeded)
                    {
                        sent++;
                    }
                    else
                    {
                        // Released for the next run.
                        _ = await memberships.GetManyQueryable(t => t.Id == offer.Id)
                            .ExecuteUpdateAsync(t => t.SetProperty(p => p.OfferEmailSentDate, (DateTimeOffset?)null));
                    }
                }

                return new(OperationResult.Succeeded) { Data = sent };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = 0, Errors = [new() { Message = exc.Message }] };
            }
        }

        internal static string? NormalizeCode(string? referralCode)
        {
            var code = referralCode?.Trim();
            return string.IsNullOrEmpty(code) || code.Length > ReferralCodeMaxLength ? null : code;
        }

        internal static string? NormalizeIdentity(string? identity)
        {
            var value = identity?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(value) || value.Length > 256 ? null : value;
        }

        /// <summary>"Sara K." - a first name and an initial, never the full name or contact details.</summary>
        internal static string ShortName(string? firstName, string? lastName)
        {
            var first = firstName?.Trim();
            var initial = string.IsNullOrEmpty(lastName?.Trim()) ? null : $"{lastName.Trim()[0]}.";
            return (first, initial) switch
            {
                (null or "", null) => "New user",
                (null or "", _) => initial,
                (_, null) => first,
                _ => $"{first} {initial}",
            };
        }

        private static string FormatPercent(decimal percent) => percent.ToString("0.##", CultureInfo.InvariantCulture);

        private static async Task<bool> IsTeacherAsync(IUnitOfWork uow, long userId) =>
            await uow.GetRepository<ApplicationUserRole>().AnyAsync(t => t.UserId == userId && t.Role!.NormalizedName == TeacherRoleName);

        private static async Task<long?> FindJoinedInviterAsync(IUnitOfWork uow, string code)
        {
            var memberships = uow.GetRepository<ReferralMembership>().GetManyQueryable();
            return await uow.GetRepository<ApplicationUser>()
                .GetManyQueryable(t => t.ReferralId == code && t.Enabled)
                .Where(t => memberships.Any(m => m.UserId == t.Id && m.JoinedDate != null))
                .Select(t => (long?)t.Id)
                .FirstOrDefaultAsync();
        }

        private async Task<ApplicationSettingsDto> GetSettingsAsync() =>
            (await applicationSettingsService.Value.GetApplicationSettingsAsync()).Data ?? new();

        /// <summary>Gives the user an invite code if they have none. The unique index on ReferralId settles a collision; it retries with a new code.</summary>
        private async Task EnsureReferralCodeAsync(long userId)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    var code = IdentityService.GenerateReferralId(userId);
                    _ = await uow.GetRepository<ApplicationUser>()
                        .GetManyQueryable(t => t.Id == userId && t.ReferralId == null)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.ReferralId, code));
                    return;
                }
                catch (UniqueConstraintException)
                {
                    // That code is taken; try another.
                }
            }
        }

        private async Task<ReferralStatusDto> BuildStatusAsync(IUnitOfWork uow, long userId)
        {
            var now = DateTimeOffset.UtcNow;
            var currentPercent = ReferralTerms.Percent(await GetSettingsAsync());
            var isTeacher = await IsTeacherAsync(uow, userId);
            var membership = await uow.GetRepository<ReferralMembership>().GetManyQueryable(t => t.UserId == userId)
                .Select(t => new { t.OfferPercent, t.OfferExpirationDate, t.JoinedDate, t.Percent })
                .FirstOrDefaultAsync();

            var status = new ReferralStatusDto
            {
                IsTeacher = isTeacher,
                Joined = membership?.JoinedDate is not null,
                Percent = membership?.Percent,
                JoinedDate = membership?.JoinedDate,
                OfferPercent = membership?.OfferPercent,
                OfferExpirationDate = membership?.OfferExpirationDate,
                OfferOpen = membership is not null && membership.JoinedDate is null && ReferralTerms.OfferOpen(membership.OfferExpirationDate, now),
                JoinPercent = membership is null
                    ? currentPercent
                    : ReferralTerms.PercentOnJoin(membership.OfferPercent, membership.OfferExpirationDate, currentPercent, now),
            };

            if (status.Joined)
            {
                var invited = uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.ReferredByUserId == userId);
                var paidKinds = uow.GetRepository<Payment>().GetManyQueryable(t => t.Status == PaymentStatus.Paid
                    && (t.Kind == PaymentKind.NewSubscription || t.Kind == PaymentKind.Renewal || t.Kind == PaymentKind.PlanSwitch));

                status.ReferralCode = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId).Select(t => t.ReferralId).FirstOrDefaultAsync();
                status.InvitedCount = await invited.CountAsync();
                status.SubscribedCount = await invited.CountAsync(t => paidKinds.Any(p => p.UserId == t.Id));
                status.EarnedUsd = await uow.GetRepository<ContentOwnerCommission>()
                    .GetManyQueryable(t => t.OwnerUserId == userId && t.Reason == CommissionReason.ReferralSubscription)
                    .SumAsync(t => (decimal?)t.AmountUsd) ?? 0m;
            }

            return status;
        }
    }
}
