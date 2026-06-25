namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Presentation.ViewModel.Experience;
    using GamaEdtech.Presentation.ViewModel.Identity;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class IdentitiesController(Lazy<ILogger<IdentitiesController>> logger, Lazy<IIdentityService> identityService)
        : ApiControllerBase<IdentitiesController>(logger)
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

                var signInResult = await identityService.Value.SignInAsync(new()
                {
                    User = authenticateResult.Data.User,
                    RememberMe = request.RememberMe,
                });

                _ = await identityService.Value.AddLoginHistoryAsync(new()
                {
                    UserId = authenticateResult.Data.User.Id,
                    IpAddress = HttpContext.GetClientIpAddress(),
                    UserAgent = HttpContext.UserAgent(),
                });

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
                RegistrationRequestDto data = new()
                {
                    Username = request.Email!,
                    Password = request.Password!,
                    Email = request.Email!,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                };
                var result = await identityService.Value.RegisterAsync(data);
                if (result.OperationResult is OperationResult.Succeeded)
                {
                    _ = BackgroundJob.Enqueue<IIdentityService>(t => t.SendRegistrationEmailAsync(new()
                    {
                        Email = data.Email,
                        Username = data.Username,
                        FirstName = data.FirstName,
                        LastName = data.LastName,
                    }));
                }

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
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateToken([NotNull] GenerateTokenRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new()
                {
                    Username = request.Username!,
                    Password = request.Password!,
                    AuthenticationProvider = AuthenticationProvider.Local,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(authenticateResult.Errors));
                }

                var result = await identityService.Value.GenerateUserTokenAsync(new()
                {
                    UserId = authenticateResult.Data.User.Id,
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });

                _ = await identityService.Value.AddLoginHistoryAsync(new()
                {
                    UserId = authenticateResult.Data.User.Id,
                    IpAddress = HttpContext.GetClientIpAddress(),
                    UserAgent = HttpContext.UserAgent(),
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
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateTokenWithOld([NotNull, FromBody] GenerateTokenWithOldRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.GenerateTokenByCoreTokenAsync(new()
                {
                    Token = request.Token,
                });
                if (result.OperationResult is not OperationResult.Succeeded || result.Data is null)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(result.Errors));
                }

                _ = await identityService.Value.AddLoginHistoryAsync(new()
                {
                    UserId = result.Data.UserId,
                    IpAddress = HttpContext.GetClientIpAddress(),
                    UserAgent = HttpContext.UserAgent(),
                });

                return Ok<GenerateTokenResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Token = result.Data.Token,
                        ExpirationTime = result.Data.ExpirationTime,
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
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateTokenWithGoogle([NotNull] GenerateTokenWithGoogleRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new()
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

                _ = await identityService.Value.AddLoginHistoryAsync(new()
                {
                    UserId = authenticateResult.Data.User.Id,
                    IpAddress = HttpContext.GetClientIpAddress(),
                    UserAgent = HttpContext.UserAgent(),
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
        [Permission(policy: null)]
        [AllowAnonymous]
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
                var result = await identityService.Value.GetProfileSettingsAsync(new IdEqualsSpecification<ApplicationUser, long>(User.UserId()));

                return Ok<ProfileSettingsResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        UserName = result.Data.UserName,
                        FirstName = result.Data.FirstName,
                        LastName = result.Data.LastName,
                        CountryId = result.Data.CountryId,
                        StateId = result.Data.StateId,
                        CityId = result.Data.CityId,
                        SchoolId = result.Data.SchoolId,
                        ReferralId = result.Data.ReferralId,
                        Gender = result.Data.Gender,
                        Grade = result.Data.Grade,
                        Board = result.Data.Board,
                        AvatarUri = result.Data.AvatarUri,
                        Group = result.Data.Group,
                        CoreId = result.Data.CoreId,
                        WalletId = result.Data.WalletId,
                        ProfileUpdated = result.Data.ProfileUpdated,
                        Roles = result.Data.Roles,
                        ProfileVisibility = result.Data.ProfileVisibility,
                        Biography = result.Data.Biography,
                        Skills = result.Data.Skills,
                        CurrentStatusSentence = result.Data.CurrentStatusSentence,
                        Experiences = result.Data.Experiences?.Select(t => new ExperienceResponseViewModel
                        {
                            Id = t.Id,
                            SchoolId = t.SchoolId,
                            SchoolTitle = t.SchoolTitle,
                            Description = t.Description,
                            StartDate = t.StartDate,
                            EndDate = t.EndDate,
                        }),
                        UserRateLevel = result.Data.UserRateLevel,
                        Handle = result.Data.Handle,
                        OrphanDate = result.Data.OrphanDate,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ProfileSettingsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("profiles/list"), Produces(typeof(ApiResponse<ListDataSource<PublicProfileListResponseViewModel>>))]
        [Permission(policy: null)]
        [AllowAnonymous]
        [Display(Name = "Get Public Profiles list")]
        public async Task<IActionResult<ListDataSource<PublicProfileListResponseViewModel>>> GetPublicProfile([NotNull, FromQuery] PublicProfileListRequestViewModel request)
        {
            try
            {
                ISpecification<ApplicationUser>? specification = new ProfileVisibilityEqualsSpecification(ProfileVisibility.Public);

                if (!string.IsNullOrEmpty(request.FullName))
                {
                    specification = specification.And(new NameContainsSpecification(request.FullName));
                }

                if (!string.IsNullOrEmpty(request.Skill))
                {
                    specification = specification.And(new SkillsContainsSpecification(request.Skill));
                }

                var result = await identityService.Value.GetProfilesListAsync(new ListRequestDto<ApplicationUser>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<PublicProfileListResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new PublicProfileListResponseViewModel
                        {
                            Avatar = t.Avatar,
                            FullName = t.FullName,
                            OnlineStatus = t.OnlineStatus,
                            Skills = t.Skills,
                            UserRateLevel = t.UserRateLevel,
                            Handle = t.Handle,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<PublicProfileListResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("profiles/{handle}"), Produces(typeof(ApiResponse<PublicProfileResponseViewModel>))]
        [Permission(policy: null)]
        [AllowAnonymous]
        [Display(Name = "Get Public Profile of a User")]
        public async Task<IActionResult<PublicProfileResponseViewModel>> GetPublicProfile([FromRoute] string handle)
        {
            try
            {
                var result = await identityService.Value.GetPublicProfileAsync(new()
                {
                    ProfileHandle = handle,
                    UserId = User.UserId(),
                });

                return Ok<PublicProfileResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        FirstName = result.Data.FirstName,
                        LastName = result.Data.LastName,
                        AvatarUri = result.Data.AvatarUri,
                        ProfileView = result.Data.ProfileView,
                        RegistrationDate = result.Data.RegistrationDate,
                        OnlineStatus = result.Data.OnlineStatus,
                        Biography = result.Data.Biography,
                        Skills = result.Data.Skills,
                        Experiences = result.Data.Experiences?.Select(t => new ExperienceResponseViewModel
                        {
                            Id = t.Id,
                            SchoolId = t.SchoolId,
                            SchoolTitle = t.SchoolTitle,
                            Description = t.Description,
                            StartDate = t.StartDate,
                            EndDate = t.EndDate,
                        }),
                        CurrentStatusSentence = result.Data.CurrentStatusSentence,
                        UserRateLevel = result.Data.UserRateLevel,
                        OrphanDate = result.Data.OrphanDate,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PublicProfileResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPut("profiles"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Update Profile Settings")]
        public async Task<IActionResult> UpdateProfileSettings([NotNull] ProfileSettingsRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.ManageProfileSettingsAsync(new()
                {
                    CityId = request.CityId,
                    SchoolId = request.SchoolId,
                    UserId = User.UserId(),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Board = request.Board,
                    Grade = request.Grade,
                    Gender = request.Gender,
                    Group = request.Group,
                    WalletId = request.WalletId,
                    ProfileVisibility = request.ProfileVisibility,
                    Avatar = request.Avatar,
                    Biography = request.Biography,
                    Skills = request.Skills,
                    CurrentStatusSentence = request.CurrentStatusSentence,
                    Handle = request.Handle,
                });

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("profiles/avatars"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        public async Task<IActionResult> ManageAvatar([NotNull] ManageAvatarRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.ManageAvatarAsync(new()
                {
                    UserId = User.UserId(),
                    Avatar = request.Avatar,
                });

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpDelete("profiles/avatars"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Remove Profile Avatar")]
        public async Task<IActionResult> RemoveAvatar()
        {
            try
            {
                var result = await identityService.Value.ManageAvatarAsync(new()
                {
                    UserId = User.UserId(),
                    Avatar = null,
                });

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("leader-board"), Produces(typeof(ApiResponse<IEnumerable<UserPointsViewModel>>))]
        [Permission(policy: null)]
        [AllowAnonymous]
        public async Task<IActionResult> GetTop100Users([FromQuery] Top100UsersRequestViewModel? request)
        {
            try
            {
                var result = await identityService.Value.GetTop100UsersAsync(new()
                {
                    Board = request?.Board,
                    Grade = request?.Grade,
                    CountryId = request?.CountryId,
                    StateId = request?.StateId,
                    CityId = request?.CityId,
                    SchoolId = request?.SchoolId,
                    RegistrationDateStart = request?.RegistrationDateStart,
                    RegistrationDateEnd = request?.RegistrationDateEnd,
                });

                return Ok<IEnumerable<UserPointsViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new UserPointsViewModel
                    {
                        Name = t.Name,
                        UserId = t.UserId,
                        Points = t.Points,
                        AvatarUri = t.AvatarUri,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<UserPointsViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpDelete("profiles"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Request Removing User Account")]
        public async Task<IActionResult<bool>> DeleteAccount([NotNull] DeleteAccountRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new()
                {
                    Username = request.Username!,
                    Password = request.Password!,
                    AuthenticationProvider = AuthenticationProvider.Local,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<bool>(new(authenticateResult.Errors));
                }

                var result = await identityService.Value.InitializeDeletingAccountAsync(new IdEqualsSpecification<ApplicationUser, long>(User.UserId()));

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("profiles/recover"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Cancel Removing User Account Request")]
        public async Task<IActionResult<bool>> RecoverAccount([NotNull] RecoverAccountRequestViewModel request)
        {
            try
            {
                var authenticateResult = await identityService.Value.AuthenticateAsync(new()
                {
                    Username = request.Username!,
                    Password = request.Password!,
                    AuthenticationProvider = AuthenticationProvider.Local,
                });
                if (authenticateResult.Data?.User is null)
                {
                    return Ok<bool>(new(authenticateResult.Errors));
                }

                var result = await identityService.Value.RecoverAccountAsync(new IdEqualsSpecification<ApplicationUser, long>(User.UserId()));

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("handles/validate"), Produces<ApiResponse<string>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<string>> ValidateHandle([FromQuery, Required] string? handle)
        {
            try
            {
                var result = await identityService.Value.ValidateHandleAsync(new()
                {
                    Handle = handle,
                    UserId = User.UserId(),
                });
                return Ok<string>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<string>(new(new Error { Message = exc.Message }));
            }
        }

    }
}

