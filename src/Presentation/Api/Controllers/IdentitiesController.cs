namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Collections.ObjectModel;
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
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Presentation.ViewModel.Experience;
    using GamaEdtech.Presentation.ViewModel.Identity;
    using GamaEdtech.Presentation.ViewModel.Subscription;

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

        /// <summary>
        /// User's dashboard payload for gamatrain-front's dashboard page, replacing its previous direct calls to
        /// gama-api's teacher/student dashboard. User/ProfileCompletion/UnreadMessages are built entirely from
        /// this backend's own data (always populated); Stats/ExamSuggestions and User.ScoreCheckInfo still have
        /// no local equivalent and stay proxied from gama-api - picked server-side from the caller's own legacy
        /// JWT group_id claim / ApplicationUser.Group, no query param needed. Never fails just because gama-api
        /// is unreachable for this caller - see DashboardResponseViewModel.LegacyDataAvailable and
        /// docs/business/identity-and-access.md, "User dashboard proxy".
        /// </summary>
        [HttpGet("dashboard"), Produces(typeof(ApiResponse<DashboardResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<DashboardResponseViewModel>> GetDashboard()
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                var result = await identityService.Value.GetDashboardAsync(User.UserId(), token);

                if (result.Data?.LegacyAuthRejected == true)
                {
                    // gama-api rejected the caller's own forwarded legacy token (401/403) even though this
                    // backend's own auth already accepted it as valid - the session may still be
                    // cryptographically valid here but is no longer honored on gama-api's side (e.g. ended via
                    // gama-api's own logout, or the account was disabled, directly on gama-api's side).
                    // Deliberately NOT degraded like every other legacy failure mode (see
                    // DashboardResponseDto.LegacyDataAvailable): propagated as a real HTTP 401, a scoped
                    // exception to this API's usual "always 200, check succeeded/errors" convention (see
                    // CLAUDE.md and UnauthorizedObjectResult{T}'s doc comment), so gamatrain-front's existing
                    // global 401/403 interceptor (useApiService.ts) re-authenticates the user, same as it
                    // already does for every other endpoint. See docs/business/identity-and-access.md, "User
                    // dashboard proxy".
                    return Unauthorized<DashboardResponseViewModel>(new(new Error { Message = "Legacy session no longer valid" }));
                }

                return Ok<DashboardResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        LegacyDataAvailable = result.Data.LegacyDataAvailable,
                        User = MapUser(result.Data.User),
                        ProfileCompletion = MapProfileCompletion(result.Data.ProfileCompletion),
                        UnreadMessages = MapUnreadMessages(result.Data.UnreadMessages),
                        Stats = MapStats(result.Data.Stats),
                        ExamSuggestions = MapExamSuggestions(result.Data.ExamSuggestions),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<DashboardResponseViewModel>(new(new Error { Message = exc.Message }));
            }

            static DashboardResponseViewModel.UserViewModel? MapUser(DashboardResponseDto.UserDto? source) => source is null ? null : new()
            {
                CoreId = source.CoreId,
                Handle = source.Handle,
                FirstName = source.FirstName,
                LastName = source.LastName,
                AvatarUri = source.AvatarUri,
                PhoneNumber = source.PhoneNumber,
                Gender = source.Gender,
                Roles = source.Roles,
                Points = source.Points,
                Enabled = source.Enabled,
                CityId = source.CityId,
                CityTitle = source.CityTitle,
                SchoolId = source.SchoolId,
                SchoolTitle = source.SchoolTitle,
                Board = source.Board,
                Grade = source.Grade,
                Subscription = MapSubscription(source.Subscription),
                ScoreCheckInfo = source.ScoreCheckInfo,
            };

            static UserSubscriptionResponseViewModel? MapSubscription(UserSubscriptionDto? source) => source is null ? null : new()
            {
                Id = source.Id,
                SubscriptionPlanId = source.SubscriptionPlanId,
                PlanTitle = source.PlanTitle,
                Status = source.Status,
                StartDate = source.StartDate,
                ExpirationDate = source.ExpirationDate,
                PricePaid = source.PricePaid,
                Currency = source.Currency,
                BillingInterval = source.BillingInterval,
                AutoRenews = source.AutoRenews,
                CancelAtPeriodEnd = source.CancelAtPeriodEnd,
                PendingSwitchPlanId = source.PendingSwitchPlanId,
                PendingSwitchPlanTitle = source.PendingSwitchPlanTitle,
                PendingSwitchBillingInterval = source.PendingSwitchBillingInterval,
                LastPaymentFailedDate = source.LastPaymentFailedDate,
                FeatureGroups = source.FeatureGroups?.Select(t => new UserSubscriptionQuotaViewModel
                {
                    Features = t.Features.Select(f => new UserSubscriptionQuotaFeatureViewModel
                    {
                        FeatureCode = f.FeatureCode,
                        FeatureName = f.FeatureName,
                        Description = f.Description,
                    }),
                    Limit = t.Limit,
                    Used = t.Used,
                    Remaining = t.Remaining,
                    Description = t.Description,
                    PlanLimits = t.PlanLimits.Select(l => new PlanFeatureLimitViewModel { BillingInterval = l.BillingInterval, Limit = l.Limit }),
                }),
            };

            static DashboardResponseViewModel.ProfileCompletionViewModel? MapProfileCompletion(DashboardResponseDto.ProfileCompletionDto? source) => source is null ? null : new()
            {
                Total = source.Total,
                Num = source.Num,
                NotComplete = MapNotComplete(source.NotComplete),
            };

            static Collection<string>? MapNotComplete(Collection<string>? source) => source is null ? null : new(source);

            static DashboardResponseViewModel.UnreadMessagesViewModel? MapUnreadMessages(DashboardResponseDto.UnreadMessagesDto? source) => source is null ? null : new() { Total = source.Total };

            static DashboardResponseViewModel.StatsViewModel? MapStats(DashboardResponseDto.StatsDto? source) => source is null ? null : new()
            {
                Test = MapStatItem(source.Test),
                File = MapStatItem(source.File),
                Question = MapStatItem(source.Question),
            };

            static DashboardResponseViewModel.StatItemViewModel? MapStatItem(DashboardResponseDto.StatItemDto? source) => source is null ? null : new() { Total = source.Total };

            static DashboardResponseViewModel.ExamSuggestionsViewModel? MapExamSuggestions(DashboardResponseDto.ExamSuggestionsDto? source) => source is null ? null : new()
            {
                Total = source.Total,
                Participated = source.Participated,
                Lessons = MapLessons(source.Lessons),
            };

            static Collection<DashboardResponseViewModel.LessonViewModel>? MapLessons(Collection<DashboardResponseDto.LessonDto>? source) => source is null ? null : new(source.Select(t => new DashboardResponseViewModel.LessonViewModel
            {
                Id = t.Id,
                Title = t.Title,
                Participated = t.Participated,
                Total = t.Total,
            }).ToList());
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

