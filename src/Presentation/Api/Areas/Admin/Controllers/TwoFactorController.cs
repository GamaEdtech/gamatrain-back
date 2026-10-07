namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.TwoFactor;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The calling admin's own authenticator-app (TOTP) second factor, required by sensitive admin actions - see
    /// ITwoFactorService and docs/business/identity-and-access.md. Flow: setup (returns key + otpauth URI to show as a
    /// QR code, after the emailed code from setup/email-code) -> enable (with a code from the app) -> send a fresh code with
    /// every protected action.
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class TwoFactorController(Lazy<ILogger<TwoFactorController>> logger, Lazy<ITwoFactorService> twoFactorService)
        : ApiControllerBase<TwoFactorController>(logger)
    {
        [HttpGet, Produces<ApiResponse<TwoFactorStatusResponseViewModel>>()]
        public async Task<IActionResult<TwoFactorStatusResponseViewModel>> GetStatus()
        {
            try
            {
                var result = await twoFactorService.Value.GetStatusAsync(User.UserId());

                return Ok<TwoFactorStatusResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new() { Enabled = result.Data.Enabled },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<TwoFactorStatusResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Step 1 of setup: emails a 6-digit code to the caller's confirmed address. Returns the masked address.</summary>
        [HttpPost("setup/email-code"), Produces<ApiResponse<string>>()]
        public async Task<IActionResult> SendSetupEmailCode()
        {
            try
            {
                var result = await twoFactorService.Value.SendSetupEmailCodeAsync(User.UserId());

                return Ok(new ApiResponse<string>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<string> { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Step 2: with the emailed code, generates a new authenticator key. Not active until confirmed via enable; refused while 2FA is already on.</summary>
        [HttpPost("setup"), Produces<ApiResponse<AuthenticatorSetupResponseViewModel>>()]
        public async Task<IActionResult<AuthenticatorSetupResponseViewModel>> BeginSetup([NotNull, FromBody] TwoFactorSetupRequestViewModel request)
        {
            try
            {
                var result = await twoFactorService.Value.BeginSetupAsync(User.UserId(), request.EmailCode);

                return Ok<AuthenticatorSetupResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        SharedKey = result.Data.SharedKey,
                        AuthenticatorUri = result.Data.AuthenticatorUri,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<AuthenticatorSetupResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("enable"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> Enable([NotNull, FromBody] TwoFactorCodeRequestViewModel request)
        {
            try
            {
                var result = await twoFactorService.Value.EnableAsync(User.UserId(), request.Code);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("disable"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> Disable([NotNull, FromBody] TwoFactorCodeRequestViewModel request)
        {
            try
            {
                var result = await twoFactorService.Value.DisableAsync(User.UserId(), request.Code);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Lost-device recovery for another user: turns their 2FA off so they can set it up again. Requires the calling admin's own code.</summary>
        [HttpPost("users/{userId:long}/reset"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> ResetForUser([FromRoute] long userId, [NotNull, FromBody] TwoFactorCodeRequestViewModel request)
        {
            try
            {
                var result = await twoFactorService.Value.ResetForUserAsync(User.UserId(), request.Code, userId);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
