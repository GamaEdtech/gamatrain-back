namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ContentReport;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// "Report a problem" on papers, exams, exam questions, tutorials, multimedia and Q&amp;A (2026-11-23; gama-api's
    /// <c>reports</c> before): users send them, staff resolve or dismiss them. See docs/business/support-and-social.md,
    /// "Content reports".
    /// </summary>
    [Injectable]
    public interface IContentReportService
    {
        /// <summary>Saves a signed-in user's report: <c>message</c> 25 to 500 characters, <c>reportType</c> 1 to 20. One open report
        /// per user and item (<c>alreadyReported</c>).</summary>
        Task<ResultData<long>> CreateAsync([NotNull] CreateContentReportRequestDto requestDto);

        Task<ResultData<ContentReportListDto>> GetReportsAsync([NotNull] ContentReportListRequestDto requestDto);

        /// <summary>Resolves or dismisses a report (or reopens it), with an optional note (staff only, never emailed). Closing an
        /// open report schedules <see cref="SendClosedEmailAsync"/>.</summary>
        Task<ResultData<bool>> SetStatusAsync(long id, [NotNull] ContentReportStatus status, string? note, long userId);

        /// <summary>Background job after a report is closed: <c>ContentReportResolvedEmailTemplate</c> or
        /// <c>ContentReportDismissedEmailTemplate</c> to the reporter. Nothing when it was reopened meanwhile. Never throws.</summary>
        Task SendClosedEmailAsync(long id);
    }
}
