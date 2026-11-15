namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.WeeklyReport;

    /// <summary>The weekly report email, a plan feature (<c>FeatureCodes.WeeklyReport</c>). See docs/business/notifications.md, "Weekly report".</summary>
    [Injectable]
    public interface IWeeklyReportService
    {
        /// <summary>
        /// The recurring job: sends last week's report (or a quiet-week reminder) to every user whose plan includes it.
        /// Does nothing unless <c>WeeklyReport:Enabled</c> is true; safe to rerun (one log row per user and week).
        /// </summary>
        Task<ResultData<WeeklyReportRunDto>> SendWeeklyReportsAsync();

        /// <summary>The email one user would get for the week holding <paramref name="weekOf"/> (default: last week), and whether it would be sent.</summary>
        Task<ResultData<WeeklyReportPreviewDto>> GetPreviewAsync(long userId, DateOnly? weekOf);

        /// <summary>Whether this server may send it (config) and whether an admin paused it (database).</summary>
        Task<ResultData<WeeklyReportStatusDto>> GetStatusAsync();

        /// <summary>Pauses or resumes the weekly report for everyone, from the next run. No deploy.</summary>
        Task<ResultData<bool>> SetPausedAsync(bool paused);

        Task<ResultData<WeeklyReportSettingsDto>> GetSettingsAsync(long userId);

        Task<ResultData<bool>> SetSubscriptionAsync(long userId, bool subscribed);

        /// <summary>The one-click link in every report: the token is the credential, so no sign-in is needed.</summary>
        Task<ResultData<bool>> UnsubscribeAsync(long userId, [NotNull] string token);
    }
}
