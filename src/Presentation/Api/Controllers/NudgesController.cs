namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Public-facing actions for the nudge system - see docs/business/notifications.md, "Nudge system". Not to
    /// be confused with Areas/Admin/Controllers/NudgesController (NudgeTemplate CRUD, Admin-only).
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class NudgesController(Lazy<ILogger<NudgesController>> logger, Lazy<INudgeService> nudgeService)
        : ApiControllerBase<NudgesController>(logger)
    {
        /// <summary>
        /// One-click unsubscribe - the link every nudge email carries. Anonymous by design: the token itself is
        /// the credential (a viewer clicking a link from their inbox is not expected to also be logged in), and
        /// it never expires, unlike Identity's own opaque bearer token. See NudgeService.UnsubscribeAsync.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("unsubscribe"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> Unsubscribe([FromQuery] long userId, [NotNull, FromQuery] string token)
        {
            try
            {
                var result = await nudgeService.Value.UnsubscribeAsync(userId, token);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// Authenticated toggle for the caller's own nudge subscription - the counterpart the one-way
        /// unsubscribe link above can't offer: a logged-in user opting back in.
        /// </summary>
        [Permission(policy: null)]
        [HttpPut("subscription"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> SetSubscription([FromQuery] bool subscribed)
        {
            try
            {
                var result = await nudgeService.Value.SetNudgeSubscriptionAsync(User.UserId(), subscribed);
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
