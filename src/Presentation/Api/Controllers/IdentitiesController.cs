namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Net.Http;
    using System.Text.Json.Serialization;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Identity;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class IdentitiesController(Lazy<ILogger<IdentitiesController>> logger, Lazy<IIdentityService> identityService
        , Lazy<UserManager<ApplicationUser>> userManager, Lazy<IHttpClientFactory> httpClientFactory) : ApiControllerBase<IdentitiesController>(logger)
    {
        [HttpPost("login"), Produces(typeof(ApiResponse<AuthenticationResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<AuthenticationResponseViewModel>> Login([NotNull] AuthenticationRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new AuthenticationRequestDto
                {
                    Username = request.Username!,
                    Password = request.Password!,
                    AuthenticationProvider = AuthenticationProvider.Local,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<AuthenticationResponseViewModel>(new(authenticateResult.Errors));
                }

                var signInResult = await identityService.Value.SignInAsync(new SignInRequestDto { RememberMe = request.RememberMe, User = authenticateResult.Data.User });
                return Ok<AuthenticationResponseViewModel>(new(signInResult.Errors)
                {
                    Data = signInResult.OperationResult is OperationResult.Succeeded ?
                    new() { Roles = signInResult.Data?.Roles?.ListToFlagsEnum<Role>(), }
                    : null,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<AuthenticationResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("register"), Produces(typeof(ApiResponse<Void>))]
        [AllowAnonymous]
        public async Task<IActionResult<Void>> Register([NotNull] RegistrationRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.RegisterAsync(new()
                {
                    Username = request.Email!,
                    Password = request.Password!,
                    Email = request.Email!,
                });

                return Ok<Void>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("logout"), Produces(typeof(ApiResponse<Void>))]
        [Permission(policy: null)]
        public async Task<IActionResult<Void>> Logout()
        {
            try
            {
                var result = await identityService.Value.SignOutAsync();

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

        [HttpPut("password"), Produces(typeof(ApiResponse<Void>))]
        [Permission(policy: null)]
        public async Task<IActionResult<Void>> ChangePassword([NotNull] ChangePasswordRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.ChangePasswordAsync(new ChangePasswordRequestDto
                {
                    CurrentPassword = request.CurrentPassword,
                    NewPassword = request.NewPassword,
                });
                return Ok<Void>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("tokens"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateToken([NotNull] GenerateTokenRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new AuthenticationRequestDto
                {
                    Username = request.Username!,
                    Password = request.Password!,
                    AuthenticationProvider = AuthenticationProvider.Local,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(authenticateResult.Errors));
                }

                var result = await identityService.Value.GenerateUserTokenAsync(new GenerateUserTokenRequestDto
                {
                    UserId = authenticateResult.Data.User.Id,
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });
                return Ok<GenerateTokenResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Token = result.Data?.Token,
                        ExpirationTime = result.Data?.ExpirationTime,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// this is temporary, must delete
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("tokens/old"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateTokenWithOld([NotNull, FromBody] GenerateTokenWithOldRequestViewModel request)
        {
            try
            {
                const string userInfoEndpoint = "https://core.gamatrain.com/api/v1/users/info";
                const string endpoint = "https://core.gamatrain.com/";
                var data = await new JsonWebTokenHandler().ValidateTokenAsync(request.Token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = endpoint,
                    RequireExpirationTime = true,
                    ValidateActor = false,
                    ValidateIssuerSigningKey = false,
                    ValidateSignatureLast = false,
                    SignatureValidator = (string token, TokenValidationParameters parameters) => new JsonWebToken(token),
                    ValidAudience = endpoint,
                });
                if (!data.IsValid)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = "Invalid Token" }));
                }

#pragma warning disable CA2000 // Dispose objects before losing scope
                var client = httpClientFactory.Value.CreateHttpClient();
#pragma warning restore CA2000 // Dispose objects before losing scope
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.Token);
                var response = await client.GetFromJsonAsync<ReponseDto>(userInfoEndpoint);
                if (response?.Data is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = "Invalid Token" }));
                }

                _ = data.Claims.TryGetValue("identity", out var email);

                var user = await userManager.Value.FindByEmailAsync(email?.ToString()!);
                if (user is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = "Invalid Token" }));
                }

                user.FirstName = response.Data.FirstName;
                user.LastName = response.Data.LastName;
                user.PhoneNumber = response.Data.Phone;
                if (!string.IsNullOrEmpty(response.Data.Avatar))
                {
                    var avatar = await client.GetByteArrayAsync(response.Data.Avatar);
                    user.Avatar = $"data:image/{Path.GetExtension(response.Data.Avatar).Trim('.')};base64,{Convert.ToBase64String(avatar)}";
                }
                _ = await userManager.Value.UpdateAsync(user);

                var result = await identityService.Value.GenerateUserTokenAsync(new GenerateUserTokenRequestDto
                {
                    UserId = user.Id,
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });
                return Ok<GenerateTokenResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Token = result.Data?.Token,
                        ExpirationTime = result.Data?.ExpirationTime,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("tokens/google"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateTokenWithGoogle([NotNull] GenerateTokenWithGoogleRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new AuthenticationRequestDto
                {
                    Username = request.Code!,
                    AuthenticationProvider = AuthenticationProvider.Google,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(authenticateResult.Errors));
                }

                var result = await identityService.Value.GenerateUserTokenAsync(new GenerateUserTokenRequestDto
                {
                    UserId = authenticateResult.Data.User.Id,
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });
                return Ok<GenerateTokenResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Token = result.Data?.Token,
                        ExpirationTime = result.Data?.ExpirationTime,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("tokens/revoke"), Produces(typeof(ApiResponse<RevokeTokenResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<RevokeTokenResponseViewModel>> RevokeToken()
        {
            try
            {
                var result = await identityService.Value.RemoveUserTokenAsync(new RemoveUserTokenRequestDto
                {
                    UserId = User.UserId(),
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });

                return Ok<RevokeTokenResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<RevokeTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("authenticated"), Produces(typeof(ApiResponse<bool>))]
        public IActionResult<bool> Authenticated()
        {
            try
            {
                return Ok<bool>(new()
                {
                    Data = User.Identity?.IsAuthenticated is true,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("profiles"), Produces(typeof(ApiResponse<ProfileSettingsResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<ProfileSettingsResponseViewModel>> GetProfileSettings()
        {
            try
            {
                var result = await identityService.Value.GetProfileSettingsAsync();

                return Ok<ProfileSettingsResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        TimeZoneId = result.Data?.TimeZoneId,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ProfileSettingsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPut("profiles"), Produces(typeof(ApiResponse<Void>))]
        [Permission(policy: null)]
        public async Task<IActionResult<Void>> UpdateProfileSettings([NotNull] ProfileSettingsRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.UpdateProfileSettingsAsync(new ProfileSettingsDto
                {
                    TimeZoneId = request.TimeZoneId,
                });

                return Ok<Void>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }

#pragma warning disable CA1034 // Nested types should not be visible
        //this is temporary, must delete
        public class ReponseDto
        {
            [JsonPropertyName("status")]
            public int Status { get; set; }

            [JsonPropertyName("data")]
            public DataDto Data { get; set; }

            public class DataDto
            {
                [JsonPropertyName("first_name")]
                public string FirstName { get; set; }

                [JsonPropertyName("last_name")]
                public string LastName { get; set; }

                [JsonPropertyName("avatar")]
                public string Avatar { get; set; }

                [JsonPropertyName("phone")]
                public string Phone { get; set; }
            }
        }
#pragma warning restore CA1034 // Nested types should not be visible
    }
}
