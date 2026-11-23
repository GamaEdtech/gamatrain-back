namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ContentReport;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// "Report a problem" with a paper, exam, exam question, tutorial, multimedia file or Q&amp;A (2026-11-23; gama-api's
    /// <c>reports</c> before). Signed-in users only. See docs/business/support-and-social.md, "Content reports".
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class ReportsController(Lazy<ILogger<ReportsController>> logger, Lazy<IContentReportService> contentReportService)
        : ApiControllerBase<ReportsController>(logger)
    {
        /// <summary>Sends a report: <c>contentType</c>, <c>contentId</c>, <c>reportType</c> (the page's list) and <c>message</c>
        /// (25 to 500 characters). One open report per user and item (<c>alreadyReported</c>). Answers the report's id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public async Task<IActionResult<long>> Create([NotNull, FromBody] CreateContentReportRequestViewModel request)
        {
            try
            {
                if (ReportedContentType.FromLegacyName(request.ContentType) is not { } type)
                {
                    return Ok<long>(new(new Error { Message = "Unknown content type.", Reference = "contentType" }));
                }

                var result = await contentReportService.Value.CreateAsync(new()
                {
                    UserId = User.UserId(),
                    ContentType = type,
                    ContentId = request.ContentId.GetValueOrDefault(),
                    ReportType = request.ReportType.GetValueOrDefault(),
                    Message = request.Message,
                });
                return Ok<long>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<long>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
