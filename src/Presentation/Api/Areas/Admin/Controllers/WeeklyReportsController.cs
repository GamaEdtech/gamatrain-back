namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.WeeklyReport;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>Admin side of the weekly report: preview any user's email for a week, exactly as the job would send it. Sends nothing.</summary>
    [Common.DataAnnotation.Area(nameof(Role.Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class WeeklyReportsController(Lazy<ILogger<WeeklyReportsController>> logger, Lazy<IWeeklyReportService> weeklyReportService)
        : ApiControllerBase<WeeklyReportsController>(logger)
    {
        /// <summary>Whether this server may send the report, and whether an admin paused it.</summary>
        [HttpGet("status"), Produces(typeof(ApiResponse<WeeklyReportStatusResponseViewModel>))]
        public async Task<IActionResult<WeeklyReportStatusResponseViewModel>> GetStatus()
        {
            try
            {
                var result = await weeklyReportService.Value.GetStatusAsync();
                return Ok<WeeklyReportStatusResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new() { EnabledOnServer = result.Data.EnabledOnServer, Paused = result.Data.Paused },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<WeeklyReportStatusResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Pauses or resumes the weekly report for everyone, from the next Monday run. No deploy.</summary>
        [HttpPut("status"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> SetPaused([FromQuery] bool paused)
        {
            try
            {
                var result = await weeklyReportService.Value.SetPausedAsync(paused);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The user's email for the week (default last week), whether the job would send it, and why not.</summary>
        [HttpGet("preview"), Produces(typeof(ApiResponse<WeeklyReportPreviewResponseViewModel>))]
        public async Task<IActionResult<WeeklyReportPreviewResponseViewModel>> GetPreview([NotNull, FromQuery] WeeklyReportPreviewRequestViewModel request)
        {
            try
            {
                var result = await weeklyReportService.Value.GetPreviewAsync(request.UserId, request.WeekOf);
                return Ok<WeeklyReportPreviewResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        HasActivity = result.Data.HasActivity,
                        WouldSend = result.Data.WouldSend,
                        NotSentReason = result.Data.NotSentReason,
                        Kind = result.Data.Email?.Kind,
                        Subject = result.Data.Email?.Subject,
                        Html = result.Data.Email?.Html,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<WeeklyReportPreviewResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
