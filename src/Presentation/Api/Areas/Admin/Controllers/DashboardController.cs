namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Mvc.Routing;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Dashboard;

    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The admin dashboard (2026-10-22): what needs attention (also used for the admin menu's count badges), the business
    /// overview, and the teacher invite program report. Read-only. See docs/business/admin-dashboard.md.
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    public class DashboardController(Lazy<ILogger<DashboardController>> logger, Lazy<IAdminDashboardService> dashboardService
        , Lazy<IEndpointDataSource> endpointDataSource)
        : ApiControllerBase<DashboardController>(logger)
    {
        // Each "needs attention" queue, and the admin list that handles it: someone who isn't an admin sees a queue's count only
        // when they can open that list (granted in admin/identities/{userId}/permissions). The queues of endpoints that are
        // never delegated (messages, school edits, payouts) are for admins only.
        private static readonly Dictionary<string, string> AttentionQueueKeys = new(StringComparer.Ordinal)
        {
            ["PastPapers"] = "Admin.PastPapers.GetPapers",
            ["Exams"] = "Admin.Exams.GetExams",
            ["ExamQuestions"] = "Admin.ExamQuestions.GetQuestions",
            ["Reports"] = "Admin.Reports.GetReports",
            ["Tutorials"] = "Admin.Tutorials.GetTutorials",
            ["Posts"] = "Admin.Posts.GetPosts",
            ["PostComments"] = "Admin.Posts.GetPostComments",
        };

        /// <summary>Whether this queue is shown: every one to an admin, else only a queue whose admin list the caller can open.</summary>
        public static bool CanSeeQueue([NotNull] string queue, bool isAdmin, [NotNull] IReadOnlySet<string> keys) =>
            isAdmin || (AttentionQueueKeys.TryGetValue(queue, out var key) && keys.Contains(key));

        /// <summary>
        /// Counts of what is waiting, with how many are new this week and the oldest one's date (the dashboard's Needs attention and
        /// the admin menu's badges). Cached for a minute. Every queue for an admin; for a user granted admin lists one by one, only
        /// the queues of those lists (e.g. papers to review with the past papers list); none for anyone else. Counts only. See
        /// docs/business/admin-dashboard.md, "Delegated staff".
        /// </summary>
        [HttpGet("summary"), Produces<ApiResponse<AdminDashboardSummaryResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<AdminDashboardSummaryResponseViewModel>> GetSummary()
        {
            try
            {
                var isAdmin = User.IsInRole(nameof(Role.Admin));
                var claims = User.Claims.Where(t => t.Type == PermissionConstants.PermissionPolicy).Select(t => (string?)t.Value).ToList();
                var roles = User.Claims.Where(t => t.Type == System.Security.Claims.ClaimTypes.Role).Select(t => (string?)t.Value).ToList();
                var keys = isAdmin
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : endpointDataSource.Value.GetEndpoints(claims, roles)?.Select(PermissionAuthorization.PermissionKey).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];
                if (!isAdmin && !AttentionQueueKeys.Values.Any(keys.Contains))
                {
                    return Ok<AdminDashboardSummaryResponseViewModel>(new() { Data = new() { GeneratedDate = DateTimeOffset.UtcNow, Items = [] } });
                }

                var result = await dashboardService.Value.GetSummaryAsync();
                return Ok<AdminDashboardSummaryResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded || result.Data is null ? null : new()
                    {
                        GeneratedDate = result.Data.GeneratedDate,
                        Items = result.Data.Items.Where(t => CanSeeQueue(t.Key, isAdmin, keys)).Select(t => new AttentionItemResponseViewModel
                        {
                            Key = t.Key,
                            Count = t.Count,
                            NewLast7Days = t.NewLast7Days,
                            OldestDate = t.OldestDate,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<AdminDashboardSummaryResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Sign-ups, revenue (USD), subscriptions and a daily series for whole UTC days; default the last 30 days.</summary>
        [HttpGet("overview"), Produces<ApiResponse<AdminDashboardOverviewResponseViewModel>>()]
        [Permission(Roles = [nameof(Role.Admin)])]
        public async Task<IActionResult<AdminDashboardOverviewResponseViewModel>> GetOverview([NotNull, FromQuery] DashboardRangeRequestViewModel request)
        {
            try
            {
                var result = await dashboardService.Value.GetOverviewAsync(request.StartDate, request.EndDate, ResolveTimeZone(request.TimeZone));
                var data = result.Data;
                return Ok<AdminDashboardOverviewResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded || data is null ? null : new()
                    {
                        From = data.From,
                        ToExclusive = data.ToExclusive,
                        SignUps = data.SignUps,
                        TeacherSignUps = data.TeacherSignUps,
                        StudentSignUps = data.StudentSignUps,
                        Revenue = data.Revenue.Select(t => new RevenueByKindResponseViewModel { Kind = t.Kind, Count = t.Count, AmountUsd = t.AmountUsd }),
                        RevenueUsd = data.RevenueUsd,
                        PaymentsWithoutUsd = data.PaymentsWithoutUsd,
                        ActiveSubscriptions = data.ActiveSubscriptions,
                        CancellingSubscriptions = data.CancellingSubscriptions,
                        NewSubscriptions = data.NewSubscriptions,
                        ExpiredSubscriptions = data.ExpiredSubscriptions,
                        ExamAttempts = data.ExamAttempts,
                        ExamsFinished = data.ExamsFinished,
                        ExamParticipants = data.ExamParticipants,
                        TopExams = data.TopExams.Select(t => new ExamParticipationByExamResponseViewModel
                        {
                            ExamId = t.ExamId,
                            Title = t.Title,
                            Attempts = t.Attempts,
                            Finished = t.Finished,
                            Participants = t.Participants,
                        }),
                        Daily = data.Daily.Select(t => new DailyPointResponseViewModel
                        {
                            Date = t.Date,
                            SignUps = t.SignUps,
                            PaidPayments = t.PaidPayments,
                            RevenueUsd = t.RevenueUsd,
                            InviteJoins = t.InviteJoins,
                            InvitedSignUps = t.InvitedSignUps,
                            ExamAttempts = t.ExamAttempts,
                            ExamsFinished = t.ExamsFinished,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<AdminDashboardOverviewResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The teacher invite program for a range: funnel, all-time top teachers, recent invited sign-ups with a self-invite flag.</summary>
        [HttpGet("referrals"), Produces<ApiResponse<AdminReferralReportResponseViewModel>>()]
        [Permission(Roles = [nameof(Role.Admin)])]
        public async Task<IActionResult<AdminReferralReportResponseViewModel>> GetReferralReport([NotNull, FromQuery] DashboardRangeRequestViewModel request)
        {
            try
            {
                var result = await dashboardService.Value.GetReferralReportAsync(request.StartDate, request.EndDate, ResolveTimeZone(request.TimeZone));
                var data = result.Data;
                return Ok<AdminReferralReportResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded || data is null ? null : new()
                    {
                        OffersCreated = data.OffersCreated,
                        OffersEmailed = data.OffersEmailed,
                        TeachersJoined = data.TeachersJoined,
                        InvitedSignUps = data.InvitedSignUps,
                        PayingInvitees = data.PayingInvitees,
                        EarnedUsd = data.EarnedUsd,
                        SharedNetworkSignUps = data.SharedNetworkSignUps,
                        TopTeachers = data.TopTeachers.Select(t => new ReferralTeacherResponseViewModel
                        {
                            UserId = t.UserId,
                            Name = t.Name,
                            JoinedDate = t.JoinedDate,
                            Percent = t.Percent,
                            Invited = t.Invited,
                            Paying = t.Paying,
                            EarnedUsd = t.EarnedUsd,
                            LastInvitedDate = t.LastInvitedDate,
                        }),
                        RecentSignUps = data.RecentSignUps.Select(t => new InvitedSignUpResponseViewModel
                        {
                            UserId = t.UserId,
                            Name = t.Name,
                            SignedUpDate = t.SignedUpDate,
                            InviterUserId = t.InviterUserId,
                            InviterName = t.InviterName,
                            Payments = t.Payments,
                            EarnedUsd = t.EarnedUsd,
                            SharedNetwork = t.SharedNetwork,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<AdminReferralReportResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        // The viewer's own zone, sent by the client (IANA or Windows id), as for finance/payments/summary. An omitted or
        // unknown id falls back to UTC rather than failing the request.
        private static TimeZoneInfo ResolveTimeZone(string? id) =>
            !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var timeZone) ? timeZone : TimeZoneInfo.Utc;
    }
}
