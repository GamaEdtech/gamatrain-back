namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.Identity;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    /// <summary>
    /// Temporary proxy to gama-api's login/register/recovery/googleAuth during the old-backend migration. login/google
    /// additionally sync the local user and hand back gama-api's own token unchanged - TokenAuthenticationHandler
    /// resolves it straight to the linked local user on later requests (see IdentityService.VerifyLegacyTokenAsync),
    /// so no gamatrain-back token is minted and gama-api needs no changes. register/recovery are pure passthroughs.
    /// Remove once the frontend migrates off gama-api.
    /// </summary>
    [Route("api/v{version:apiVersion}/legacy-auth")]
    [ApiVersion("1.0")]
    [AllowAnonymous]
    public class LegacyAuthBridgeController(Lazy<ILogger<LegacyAuthBridgeController>> logger, Lazy<IIdentityService> identityService)
        : ApiControllerBase<LegacyAuthBridgeController>(logger)
    {
        [HttpPost("login"), Produces(typeof(ApiResponse<LegacyAuthTokenResponseViewModel>))]
        public async Task<IActionResult<LegacyAuthTokenResponseViewModel>> Login([NotNull] LegacyLoginRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.LegacyLoginAsync(new()
                {
                    Identity = request.Identity!,
                    Password = request.Password,
                    Type = request.Type,
                    Code = request.Code,
                });

                return Ok<LegacyAuthTokenResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is OperationResult.Succeeded && result.Data is not null
                        ? new() { Type = result.Data.Type, Token = result.Data.Token, ExpirationTime = result.Data.ExpirationTime, }
                        : null,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<LegacyAuthTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("google"), Produces(typeof(ApiResponse<LegacyAuthTokenResponseViewModel>))]
        public async Task<IActionResult<LegacyAuthTokenResponseViewModel>> Google([NotNull] LegacyGoogleAuthRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.LegacyGoogleAuthAsync(new()
                {
                    IdToken = request.IdToken!,
                });

                return Ok<LegacyAuthTokenResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is OperationResult.Succeeded && result.Data is not null
                        ? new() { Token = result.Data.Token, ExpirationTime = result.Data.ExpirationTime, }
                        : null,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<LegacyAuthTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("register"), Produces(typeof(ApiResponse<LegacyMessageResponseViewModel>))]
        public async Task<IActionResult<LegacyMessageResponseViewModel>> Register([NotNull] LegacyOtpFlowRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.LegacyRegisterAsync(new()
                {
                    Type = request.Type!,
                    Identity = request.Identity!,
                    Code = request.Code,
                    Password = request.Password,
                });

                return Ok<LegacyMessageResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new() { Message = result.Data.Message, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<LegacyMessageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("recovery"), Produces(typeof(ApiResponse<LegacyMessageResponseViewModel>))]
        public async Task<IActionResult<LegacyMessageResponseViewModel>> Recovery([NotNull] LegacyOtpFlowRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.LegacyRecoveryAsync(new()
                {
                    Type = request.Type!,
                    Identity = request.Identity!,
                    Code = request.Code,
                    Password = request.Password,
                });

                return Ok<LegacyMessageResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new() { Message = result.Data.Message, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<LegacyMessageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// Ends the caller's gama-api session by proxying to gama-api's own GET /users/logout with their raw legacy
        /// JWT. Pure passthrough, same as Register/Recovery - this backend never stored the token, so there's
        /// nothing local to invalidate; gama-api itself validates and revokes it. Not to be confused with
        /// IdentitiesController.Logout (Identity cookie) or tokens/revoke (opaque bearer token), neither of which
        /// can end a legacy-bridge session - see authentication.md.
        /// </summary>
        [HttpGet("logout"), Produces(typeof(ApiResponse<Void>))]
        public async Task<IActionResult<Void>> Logout()
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<Void>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await identityService.Value.LegacyLogoutAsync(token);

                return Ok<Void>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
