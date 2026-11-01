namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Dashboard;

    /// <summary>
    /// The admin dashboard (2026-10-22): what needs attention, the business overview, and the teacher invite program report.
    /// Read-only. See docs/business/admin-dashboard.md.
    /// </summary>
    [Injectable]
    public interface IAdminDashboardService
    {
        /// <summary>The "needs attention" queues (unread contact us, content to review, payouts waiting). Cached for a minute.</summary>
        Task<ResultData<AdminDashboardSummaryDto>> GetSummaryAsync();

        /// <summary>Sign-ups, revenue, subscriptions and daily trends for whole UTC days [startDate, endDate]; default the last 30 days.</summary>
        Task<ResultData<AdminDashboardOverviewDto>> GetOverviewAsync(DateOnly? startDate, DateOnly? endDate, TimeZoneInfo? timeZone = null);

        /// <summary>The teacher invite program funnel, top teachers and recent invited sign-ups for the same kind of range.</summary>
        Task<ResultData<AdminReferralReportDto>> GetReferralReportAsync(DateOnly? startDate, DateOnly? endDate, TimeZoneInfo? timeZone = null);
    }
}
