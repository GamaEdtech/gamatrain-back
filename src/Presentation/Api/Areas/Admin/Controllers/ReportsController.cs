namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ContentReport;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ContentReport;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The "report a problem" list (papers, exams, questions, tutorials, multimedia, Q&amp;A) for staff to resolve or dismiss.
    /// Delegable (content staff): it shows the reporter's name, never their email. See docs/business/support-and-social.md,
    /// "Content reports".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class ReportsController(Lazy<ILogger<ReportsController>> logger, Lazy<IContentReportService> contentReportService)
        : ApiControllerBase<ReportsController>(logger)
    {
        /// <summary>Reports, newest first: <c>status</c> (Open, Resolved, Dismissed), <c>contentType</c>, <c>contentId</c>,
        /// <c>page</c>, <c>pageSize</c> (max 100). Each with the item's title when it lives here.</summary>
        [HttpGet(""), Produces<ApiResponse<ContentReportListDto>>()]
        public async Task<IActionResult<ContentReportListDto>> GetReports([FromQuery] string? status, [FromQuery] string? contentType,
            [FromQuery] long? contentId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                ContentReportStatus? reportStatus = null;
                if (!string.IsNullOrWhiteSpace(status) && !status.TryGetFromNameOrValue<ContentReportStatus, byte>(out reportStatus))
                {
                    return Ok<ContentReportListDto>(new(new Error { Message = "Unknown status.", Reference = "status" }));
                }

                ReportedContentType? type = null;
                if (!string.IsNullOrWhiteSpace(contentType) && (type = ReportedContentType.FromLegacyName(contentType)) is null)
                {
                    return Ok<ContentReportListDto>(new(new Error { Message = "Unknown content type.", Reference = "contentType" }));
                }

                var result = await contentReportService.Value.GetReportsAsync(new()
                {
                    Status = reportStatus,
                    ContentType = type,
                    ContentId = contentId,
                    Page = page,
                    PageSize = pageSize,
                });
                return Ok<ContentReportListDto>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ContentReportListDto>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Resolves, dismisses or reopens a report, with an optional note.</summary>
        [HttpPut("{id:long}/status"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> SetStatus([FromRoute] long id, [NotNull, FromBody] ContentReportStatusRequestViewModel request)
        {
            try
            {
                if (!request.Status.TryGetFromNameOrValue<ContentReportStatus, byte>(out var status) || status is null)
                {
                    return Ok<bool>(new(new Error { Message = "Unknown status.", Reference = "status" }));
                }

                var result = await contentReportService.Value.SetStatusAsync(id, status, request.Note, User.UserId());
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
