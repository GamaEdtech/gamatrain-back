namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.WeeklyReport;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The user's side of the weekly report (a plan feature): their setting, and the one-click unsubscribe link every
    /// report carries. See docs/business/notifications.md, "Weekly report". Admin preview: Areas/Admin WeeklyReportsController.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class WeeklyReportsController(Lazy<ILogger<WeeklyReportsController>> logger, Lazy<IWeeklyReportService> weeklyReportService)
        : ApiControllerBase<WeeklyReportsController>(logger)
    {
        [Permission(policy: null)]
        [HttpGet("settings"), Produces(typeof(ApiResponse<WeeklyReportSettingsResponseViewModel>))]
        public async Task<IActionResult<WeeklyReportSettingsResponseViewModel>> GetSettings()
        {
            try
            {
                var result = await weeklyReportService.Value.GetSettingsAsync(User.UserId());
                return Ok<WeeklyReportSettingsResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Subscribed = result.Data.Subscribed,
                        IncludedInPlan = result.Data.IncludedInPlan,
                        Email = result.Data.Email,
                        EmailConfirmed = result.Data.EmailConfirmed,
                        LastSentDate = result.Data.LastSentDate,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<WeeklyReportSettingsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Turns the caller's own weekly report on or off.</summary>
        [Permission(policy: null)]
        [HttpPut("settings"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> SetSubscription([FromQuery] bool subscribed)
        {
            try
            {
                var result = await weeklyReportService.Value.SetSubscriptionAsync(User.UserId(), subscribed);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// The link in every report. Anonymous by design: the token is the credential (whoever clicks a link in their inbox
        /// isn't expected to be signed in), and it doesn't expire. Separate from the nudges' link: it stops only the report.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("unsubscribe"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> Unsubscribe([FromQuery] long userId, [NotNull, FromQuery] string token)
        {
            try
            {
                var result = await weeklyReportService.Value.UnsubscribeAsync(userId, token);
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
