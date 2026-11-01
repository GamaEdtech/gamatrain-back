namespace GamaEdtech.Application.Service
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Dashboard;
    using GamaEdtech.Data.Dto.Referral;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>Admin dashboard reports. Read-only; see IAdminDashboardService and docs/business/admin-dashboard.md.</summary>
    public class AdminDashboardService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<AdminDashboardService>> localizer, Lazy<ILogger<AdminDashboardService>> logger, Lazy<ICacheProvider> cacheProvider)
        : LocalizableServiceBase<AdminDashboardService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IAdminDashboardService
    {
        private const string SummaryCacheKey = "AdminDashboardSummary";
        private const int TopTeachersCount = 20;
        private const int RecentSignUpsCount = 50;
        private static readonly TimeSpan SummaryCacheLifetime = TimeSpan.FromMinutes(1);
        private static readonly string TeacherRoleName = nameof(Role.Teacher).ToUpperInvariant();
        private static readonly string StudentRoleName = nameof(Role.Student).ToUpperInvariant();

        public async Task<ResultData<AdminDashboardSummaryDto>> GetSummaryAsync()
        {
            try
            {
                var summary = await cacheProvider.Value.GetAsync(SummaryCacheKey, BuildSummaryAsync,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SummaryCacheLifetime });
                return new(OperationResult.Succeeded) { Data = summary };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<AdminDashboardOverviewDto>> GetOverviewAsync(DateOnly? startDate, DateOnly? endDate, TimeZoneInfo? timeZone = null)
        {
            try
            {
                var zone = timeZone ?? TimeZoneInfo.Utc;
                var (from, to) = DashboardRange.Resolve(startDate, endDate, DashboardRange.LocalDay(DateTimeOffset.UtcNow, zone), zone);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var users = uow.GetRepository<ApplicationUser>().GetManyQueryable();
                var userRoles = uow.GetRepository<ApplicationUserRole>().GetManyQueryable();

                var signUps = await users.Where(t => t.RegistrationDate >= from && t.RegistrationDate < to)
                    .Select(t => new
                    {
                        Date = t.RegistrationDate!.Value,
                        IsTeacher = userRoles.Any(r => r.UserId == t.Id && r.Role!.NormalizedName == TeacherRoleName),
                        IsStudent = userRoles.Any(r => r.UserId == t.Id && r.Role!.NormalizedName == StudentRoleName),
                    })
                    .ToListAsync();

                // A payment counts on the day it was confirmed (VerifyDate), which is when the money came in.
                var payments = await uow.GetRepository<Payment>()
                    .GetManyQueryable(t => t.Status == PaymentStatus.Paid && (t.VerifyDate ?? t.CreationDate) >= from && (t.VerifyDate ?? t.CreationDate) < to)
                    .Select(t => new { t.Kind, t.Currency, t.Amount, t.BaseCurrencyAmount, Date = t.VerifyDate ?? t.CreationDate })
                    .ToListAsync();
                var paymentsUsd = payments.Select(t => new { t.Kind, t.Date, Usd = ReferralTerms.PaymentUsd(t.Currency.Name, t.Amount, t.BaseCurrencyAmount) }).ToList();

                var subscriptions = uow.GetRepository<UserSubscription>().GetManyQueryable();
                var activeSubscriptions = await subscriptions.CountAsync(t => t.Status == UserSubscriptionStatus.Active);
                var cancellingSubscriptions = await subscriptions.CountAsync(t => t.Status == UserSubscriptionStatus.Active && t.CancelAtPeriodEnd);
                var expiredSubscriptions = await subscriptions.CountAsync(t => t.Status == UserSubscriptionStatus.Expired && t.ExpirationDate >= from && t.ExpirationDate < to);

                var inviteJoins = await uow.GetRepository<ReferralMembership>()
                    .GetManyQueryable(t => t.JoinedDate >= from && t.JoinedDate < to)
                    .Select(t => t.JoinedDate!.Value)
                    .ToListAsync();
                var invitedSignUps = await users.Where(t => t.ReferredDate >= from && t.ReferredDate < to)
                    .Select(t => t.ReferredDate!.Value)
                    .ToListAsync();

                // The day in the viewer's time zone, so a sign-up at 01:00 Tehran time counts on that day, not the one before.
                DateOnly Day(DateTimeOffset date) => DashboardRange.LocalDay(date, zone);

                var overview = new AdminDashboardOverviewDto
                {
                    From = from,
                    ToExclusive = to,
                    SignUps = signUps.Count,
                    TeacherSignUps = signUps.Count(t => t.IsTeacher),
                    StudentSignUps = signUps.Count(t => t.IsStudent),
                    Revenue = [.. paymentsUsd
                        .GroupBy(t => t.Kind)
                        .Select(g => new RevenueByKindDto { Kind = g.Key, Count = g.Count(), AmountUsd = g.Sum(t => t.Usd ?? 0m) })
                        .OrderByDescending(t => t.AmountUsd)],
                    RevenueUsd = paymentsUsd.Sum(t => t.Usd ?? 0m),
                    PaymentsWithoutUsd = paymentsUsd.Count(t => t.Usd is null),
                    ActiveSubscriptions = activeSubscriptions,
                    CancellingSubscriptions = cancellingSubscriptions,
                    NewSubscriptions = paymentsUsd.Count(t => t.Kind == PaymentKind.NewSubscription),
                    ExpiredSubscriptions = expiredSubscriptions,
                    Daily = [.. DashboardRange.Days(from, to, zone).Select(day => new DailyPointDto
                    {
                        Date = day,
                        SignUps = signUps.Count(t => Day(t.Date) == day),
                        PaidPayments = paymentsUsd.Count(t => Day(t.Date) == day),
                        RevenueUsd = paymentsUsd.Where(t => Day(t.Date) == day).Sum(t => t.Usd ?? 0m),
                        InviteJoins = inviteJoins.Count(t => Day(t) == day),
                        InvitedSignUps = invitedSignUps.Count(t => Day(t) == day),
                    })],
                };

                return new(OperationResult.Succeeded) { Data = overview };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<AdminReferralReportDto>> GetReferralReportAsync(DateOnly? startDate, DateOnly? endDate, TimeZoneInfo? timeZone = null)
        {
            try
            {
                var zone = timeZone ?? TimeZoneInfo.Utc;
                var (from, to) = DashboardRange.Resolve(startDate, endDate, DashboardRange.LocalDay(DateTimeOffset.UtcNow, zone), zone);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var memberships = uow.GetRepository<ReferralMembership>().GetManyQueryable();
                var users = uow.GetRepository<ApplicationUser>().GetManyQueryable();
                var referralCommissions = uow.GetRepository<ContentOwnerCommission>().GetManyQueryable(t => t.Reason == CommissionReason.ReferralSubscription);
                var sightings = uow.GetRepository<ReferralIpSighting>().GetManyQueryable();

                var invitedInRange = users.Where(t => t.ReferredByUserId != null && t.ReferredDate >= from && t.ReferredDate < to);
                var sharedNetwork = invitedInRange.Where(t => sightings.Any(s => s.UserId == t.Id
                    && sightings.Any(i => i.UserId == t.ReferredByUserId && i.IpHash == s.IpHash)));

                var report = new AdminReferralReportDto
                {
                    OffersCreated = await memberships.CountAsync(t => t.OfferDate >= from && t.OfferDate < to),
                    // An offer created by joining straight from the dashboard is marked sent at the join moment without an email.
                    OffersEmailed = await memberships.CountAsync(t => t.OfferEmailSentDate >= from && t.OfferEmailSentDate < to
                        && (t.JoinedDate == null || t.JoinedDate > t.OfferEmailSentDate)),
                    TeachersJoined = await memberships.CountAsync(t => t.JoinedDate >= from && t.JoinedDate < to),
                    InvitedSignUps = await invitedInRange.CountAsync(),
                    PayingInvitees = await referralCommissions.Where(t => t.CreationDate >= from && t.CreationDate < to)
                        .Select(t => t.DownloaderUserId).Distinct().CountAsync(),
                    EarnedUsd = await referralCommissions.Where(t => t.CreationDate >= from && t.CreationDate < to)
                        .SumAsync(t => (decimal?)t.AmountUsd) ?? 0m,
                    SharedNetworkSignUps = await sharedNetwork.CountAsync(),
                };

                var teachers = await memberships.Where(t => t.JoinedDate != null)
                    .Select(t => new
                    {
                        t.UserId,
                        t.User!.FirstName,
                        t.User.LastName,
                        t.JoinedDate,
                        t.Percent,
                        Invited = users.Count(u => u.ReferredByUserId == t.UserId),
                        Paying = referralCommissions.Where(c => c.OwnerUserId == t.UserId).Select(c => c.DownloaderUserId).Distinct().Count(),
                        EarnedUsd = referralCommissions.Where(c => c.OwnerUserId == t.UserId).Sum(c => (decimal?)c.AmountUsd) ?? 0m,
                        LastInvitedDate = users.Where(u => u.ReferredByUserId == t.UserId).Max(u => u.ReferredDate),
                    })
                    .OrderByDescending(t => t.EarnedUsd).ThenByDescending(t => t.Invited).ThenBy(t => t.JoinedDate)
                    .Take(TopTeachersCount)
                    .ToListAsync();
                report.TopTeachers = [.. teachers.Select(t => new ReferralTeacherDto
                {
                    UserId = t.UserId,
                    Name = FullName(t.FirstName, t.LastName),
                    JoinedDate = t.JoinedDate,
                    Percent = t.Percent,
                    Invited = t.Invited,
                    Paying = t.Paying,
                    EarnedUsd = t.EarnedUsd,
                    LastInvitedDate = t.LastInvitedDate,
                })];

                var recent = await invitedInRange
                    .OrderByDescending(t => t.ReferredDate)
                    .Take(RecentSignUpsCount)
                    .Select(t => new
                    {
                        t.Id,
                        t.FirstName,
                        t.LastName,
                        t.ReferredDate,
                        InviterUserId = t.ReferredByUserId!.Value,
                        InviterFirstName = t.ReferredBy!.FirstName,
                        InviterLastName = t.ReferredBy.LastName,
                        Payments = referralCommissions.Count(c => c.DownloaderUserId == t.Id),
                        EarnedUsd = referralCommissions.Where(c => c.DownloaderUserId == t.Id).Sum(c => (decimal?)c.AmountUsd) ?? 0m,
                        SharedNetwork = sightings.Any(s => s.UserId == t.Id && sightings.Any(i => i.UserId == t.ReferredByUserId && i.IpHash == s.IpHash)),
                    })
                    .ToListAsync();
                report.RecentSignUps = [.. recent.Select(t => new InvitedSignUpDto
                {
                    UserId = t.Id,
                    Name = FullName(t.FirstName, t.LastName),
                    SignedUpDate = t.ReferredDate,
                    InviterUserId = t.InviterUserId,
                    InviterName = FullName(t.InviterFirstName, t.InviterLastName),
                    Payments = t.Payments,
                    EarnedUsd = t.EarnedUsd,
                    SharedNetwork = t.SharedNetwork,
                })];

                return new(OperationResult.Succeeded) { Data = report };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private static string? FullName(string? firstName, string? lastName)
        {
            var name = $"{firstName} {lastName}".Trim();
            return name.Length == 0 ? null : name;
        }

        private async Task<AdminDashboardSummaryDto?> BuildSummaryAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
            var contributions = uow.GetRepository<Contribution>().GetManyQueryable(t => t.Status == Status.Review);

            // Each query reads only waiting rows through a small filtered index, so the whole summary is a few milliseconds.
            var items = new List<AttentionItemDto>
            {
                await QueueAsync("ContactUs", uow.GetRepository<Ticket>()
                    .GetManyQueryable(t => !t.IsReadByAdmin && t.Status != TicketStatus.Closed && t.Status != TicketStatus.NotImportant)
                    .Select(t => t.LastActivityDate)),
                await QueueAsync("SchoolImages", contributions.Where(t => t.CategoryType == CategoryType.SchoolImage).Select(t => t.CreationDate)),
                await QueueAsync("SchoolContributions", contributions.Where(t => t.CategoryType == CategoryType.School).Select(t => t.CreationDate)),
                await QueueAsync("SchoolIssues", contributions.Where(t => t.CategoryType == CategoryType.SchoolIssues).Select(t => t.CreationDate)),
                await QueueAsync("SchoolImageRemovals", contributions.Where(t => t.CategoryType == CategoryType.RemoveSchoolImage).Select(t => t.CreationDate)),
                await QueueAsync("SchoolComments", contributions.Where(t => t.CategoryType == CategoryType.SchoolComment).Select(t => t.CreationDate)),
                await QueueAsync("Posts", uow.GetRepository<Post>().GetManyQueryable(t => t.Status == Status.Review).Select(t => t.CreationDate)),
                await QueueAsync("PostComments", uow.GetRepository<PostComment>().GetManyQueryable(t => t.Status == Status.Review).Select(t => t.CreationDate)),
                await QueueAsync("Payouts", uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Status == PayoutStatus.Pending || t.Status == PayoutStatus.Approved)
                    .Select(t => t.CreationDate)),
            };

            return new AdminDashboardSummaryDto { Items = items, GeneratedDate = DateTimeOffset.UtcNow };

            async Task<AttentionItemDto> QueueAsync(string key, IQueryable<DateTimeOffset> dates)
            {
                var stats = await dates.GroupBy(_ => 1)
                    .Select(g => new { Count = g.Count(), New = g.Count(d => d >= weekAgo), Oldest = g.Min() })
                    .FirstOrDefaultAsync();
                return new AttentionItemDto
                {
                    Key = key,
                    Count = stats?.Count ?? 0,
                    NewLast7Days = stats?.New ?? 0,
                    OldestDate = stats?.Oldest,
                };
            }
        }
    }
}
