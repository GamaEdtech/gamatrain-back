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
    using GamaEdtech.Common.Mvc.Routing;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Data.Dto.ProfileProgress;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Presentation.ViewModel.Experience;
    using GamaEdtech.Presentation.ViewModel.Dashboard;
    using GamaEdtech.Presentation.ViewModel.Identity;
    using GamaEdtech.Presentation.ViewModel.ProfileProgress;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class IdentitiesController(Lazy<ILogger<IdentitiesController>> logger, Lazy<IIdentityService> identityService, Lazy<IReferralService> referralService
        , Lazy<IEndpointDataSource> endpointDataSource, Lazy<IProfileProgressService> profileProgressService, Lazy<IAdminDashboardService> dashboardService)
        : ApiControllerBase<IdentitiesController>(logger)
    {
        // Each "needs attention" queue, and the admin list that handles it: a caller sees a queue's count only when they can open
        // that list. The queues of endpoints that are never delegated (messages, school edits, payouts) need the admin dashboard
        // itself, which only admins can open.
        private const string DashboardKey = "Admin.Dashboard.GetSummary";

        /// <summary>Whether a caller holding these admin endpoint keys sees any "needs attention" queue.</summary>
        public static bool CanSeeAttention([NotNull] IReadOnlySet<string> keys) => keys.Contains(DashboardKey) || AttentionQueueKeys.Values.Any(keys.Contains);

        /// <summary>Whether a caller holding these admin endpoint keys sees this queue: all of them with the admin dashboard, else
        /// only a queue whose admin list they can open.</summary>
        public static bool CanSeeQueue([NotNull] string queue, [NotNull] IReadOnlySet<string> keys) =>
            keys.Contains(DashboardKey) || (AttentionQueueKeys.TryGetValue(queue, out var key) && keys.Contains(key));

        private static readonly Dictionary<string, string> AttentionQueueKeys = new(StringComparer.Ordinal)
        {
            ["PastPapers"] = "Admin.PastPapers.GetPapers",
            ["Exams"] = "Admin.Exams.GetExams",
            ["ExamQuestions"] = "Admin.ExamQuestions.GetQuestions",
            ["Tutorials"] = "Admin.Tutorials.GetTutorials",
            ["Posts"] = "Admin.Posts.GetPosts",
            ["PostComments"] = "Admin.Posts.GetPostComments",
        };

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

        /// <summary>Signing up, step 1 (as gama-api's register "request"/"resend_code"): emails a 6-digit code to an address
        /// that has no account. Answers the address, masked. See docs/business/identity-and-access.md, "Sign-up and password reset
        /// with an emailed code".</summary>
        [HttpPost("register/code"), Produces(typeof(ApiResponse<string>))]
        [AllowAnonymous]
        public Task<IActionResult<string>> SendRegistrationCode([NotNull] EmailCodeRequestViewModel request) =>
            AnswerAsync(() => identityService.Value.SendRegistrationCodeAsync(request.Email!));

        /// <summary>Signing up, step 2 (gama-api's "confirm"): checks the code before the password is chosen. Doesn't use it up.</summary>
        [HttpPost("register/code/verify"), Produces(typeof(ApiResponse<Void>))]
        [AllowAnonymous]
        public Task<IActionResult<Void>> VerifyRegistrationCode([NotNull] EmailCodeCheckRequestViewModel request) =>
            AnswerAsync(() => identityService.Value.VerifyRegistrationCodeAsync(request.Email!, request.Code));

        /// <summary>Signing up, step 3: the code again and the password. Creates the account with a confirmed email and signs the
        /// person in (answers this backend's token).</summary>
        [HttpPost("register"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> Register([NotNull] RegistrationRequestViewModel request)
        {
            try
            {
                RegistrationRequestDto data = new()
                {
                    Username = request.Email!.Trim(),
                    Password = request.Password!,
                    Email = request.Email!.Trim(),
                    Code = request.Code!,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    ReferralCode = request.ReferralCode,
                };
                var result = await identityService.Value.RegisterAsync(data);
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<GenerateTokenResponseViewModel>(new(result.Errors));
                }

                _ = BackgroundJob.Enqueue<IIdentityService>(t => t.SendRegistrationEmailAsync(new()
                {
                    Email = data.Email,
                    Username = data.Username,
                    FirstName = data.FirstName,
                    LastName = data.LastName,
                }));

                return await IssueTokenAsync(result.Data);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Sign-in with an emailed code, step 1 (gama-api's loginByOTP): emails a 6-digit code to an account's address. The
        /// website asks for it after a wrong password. Answers the address, masked.</summary>
        [HttpPost("login/code"), Produces(typeof(ApiResponse<string>))]
        [AllowAnonymous]
        public Task<IActionResult<string>> SendLoginCode([NotNull] EmailCodeRequestViewModel request) =>
            AnswerAsync(() => identityService.Value.SendLoginCodeAsync(request.Email!));

        /// <summary>Sign-in with an emailed code, step 2: the code instead of the password. Answers this backend's token.</summary>
        [HttpPost("tokens/code"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> GenerateTokenWithCode([NotNull] EmailCodeCheckRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.SignInWithCodeAsync(request.Email!, request.Code);
                return result.OperationResult is OperationResult.Succeeded && result.Data > 0
                    ? await IssueTokenAsync(result.Data)
                    : Ok<GenerateTokenResponseViewModel>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Forgotten password, step 1 (gama-api's recovery "request"/"resend_code"): emails a 6-digit code to an account's
        /// address. Answers the address, masked.</summary>
        [HttpPost("password/reset-code"), Produces(typeof(ApiResponse<string>))]
        [AllowAnonymous]
        public Task<IActionResult<string>> SendPasswordResetCode([NotNull] EmailCodeRequestViewModel request) =>
            AnswerAsync(() => identityService.Value.SendPasswordResetCodeAsync(request.Email!));

        /// <summary>Forgotten password, step 2 (gama-api's "confirm"): checks the code before the new password. Doesn't use it up.</summary>
        [HttpPost("password/reset-code/verify"), Produces(typeof(ApiResponse<Void>))]
        [AllowAnonymous]
        public Task<IActionResult<Void>> VerifyPasswordResetCode([NotNull] EmailCodeCheckRequestViewModel request) =>
            AnswerAsync(() => identityService.Value.VerifyPasswordResetCodeAsync(request.Email!, request.Code));

        /// <summary>Forgotten password, step 3: the code again and the new password. Ends the user's other sessions and signs them in
        /// here (answers this backend's token).</summary>
        [HttpPost("password/reset"), Produces(typeof(ApiResponse<GenerateTokenResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<GenerateTokenResponseViewModel>> ResetPasswordWithCode([NotNull] PasswordResetWithCodeRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.ResetPasswordWithCodeAsync(request.Email!, request.Code, request.Password!);
                return result.OperationResult is OperationResult.Succeeded && result.Data > 0
                    ? await IssueTokenAsync(result.Data)
                    : Ok<GenerateTokenResponseViewModel>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GenerateTokenResponseViewModel>(new(new Error { Message = exc.Message }));
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
                // Only this backend's password. gama-api is never asked: asking it with a wrong password made it email its own code
                // (or, for an email with no account there, a sign-up code). A password only gama-api knows signs in with an emailed
                // code instead (login/code), which the website offers after a wrong password.
                return authenticateResult.Data?.User?.Id is long id
                    ? await IssueTokenAsync(id)
                    : Ok<GenerateTokenResponseViewModel>(new(authenticateResult.Errors));
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

                if (authenticateResult.Data.IsNewUser)
                {
                    // Teacher invite link (docs/business/referrals.md): only for the sign-in that created the account.
                    _ = await referralService.Value.BindInviterAsync(authenticateResult.Data.User.Id, request.ReferralCode);
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
        /// The role-gated (area) endpoints the caller may use, as "Area.Controller.Action" keys (e.g.
        /// "Admin.Posts.GetPosts"): every endpoint of an area their role opens, plus each endpoint granted to them one by
        /// one in admin/identities/{userId}/permissions. The admin panel uses it to show only the menus and pages the
        /// caller can open; it is not the security check itself, every endpoint still enforces its own permission.
        /// See docs/business/identity-and-access.md, "Delegated admin permissions".
        /// </summary>
        [HttpGet("permissions"), Produces(typeof(ApiResponse<IEnumerable<string>>))]
        [Permission(policy: null)]
        public IActionResult<IEnumerable<string>> GetMyPermissions()
        {
            try
            {
                var claims = User.Claims.Where(t => t.Type == PermissionConstants.PermissionPolicy).Select(t => (string?)t.Value).ToList();
                var roles = User.Claims.Where(t => t.Type == System.Security.Claims.ClaimTypes.Role).Select(t => (string?)t.Value).ToList();
                var keys = endpointDataSource.Value.GetEndpoints(claims, roles)?
                    .Select(PermissionAuthorization.PermissionKey)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList() ?? [];

                return Ok<IEnumerable<string>>(new()
                {
                    Data = keys,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<string>>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// The admin dashboard's "needs attention" counts the caller may see (the admin menu's badges): every queue for an admin,
        /// and for a user granted admin lists one by one (admin/identities/{userId}/permissions) only the queues of those lists,
        /// e.g. papers to review with the past papers list. Counts only, the same as <c>admin/dashboard/summary</c>; empty for
        /// anyone else. See docs/business/admin-dashboard.md.
        /// </summary>
        [HttpGet("attention"), Produces(typeof(ApiResponse<AdminDashboardSummaryResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<AdminDashboardSummaryResponseViewModel>> GetMyAttention()
        {
            try
            {
                var claims = User.Claims.Where(t => t.Type == PermissionConstants.PermissionPolicy).Select(t => (string?)t.Value).ToList();
                var roles = User.Claims.Where(t => t.Type == System.Security.Claims.ClaimTypes.Role).Select(t => (string?)t.Value).ToList();
                var keys = endpointDataSource.Value.GetEndpoints(claims, roles)?
                    .Select(PermissionAuthorization.PermissionKey).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];
                if (!CanSeeAttention(keys))
                {
                    return Ok<AdminDashboardSummaryResponseViewModel>(new() { Data = new() { GeneratedDate = DateTimeOffset.UtcNow, Items = [] } });
                }

                var result = await dashboardService.Value.GetSummaryAsync();
                return Ok<AdminDashboardSummaryResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded || result.Data is null ? null : new()
                    {
                        GeneratedDate = result.Data.GeneratedDate,
                        Items = [.. result.Data.Items
                            .Where(t => CanSeeQueue(t.Key, keys))
                            .Select(t => new AttentionItemResponseViewModel { Key = t.Key, Count = t.Count, NewLast7Days = t.NewLast7Days, OldestDate = t.OldestDate })],
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<AdminDashboardSummaryResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// The user's dashboard (gamatrain-front's dashboard page), built from this backend's own tables. See
        /// docs/business/identity-and-access.md, "User dashboard".
        /// </summary>
        [HttpGet("dashboard"), Produces(typeof(ApiResponse<DashboardResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<DashboardResponseViewModel>> GetDashboard()
        {
            try
            {
                var result = await identityService.Value.GetDashboardAsync(User.UserId());

                return Ok<DashboardResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
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
                Exam = MapStatItem(source.Exam),
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
                        Email = result.Data.Email,
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

        /// <summary>The caller's own progress for the profile page: level and quests, points and rank, practice streak,
        /// and their school's shared goal. See docs/business/identity-and-access.md, "Profile progress".</summary>
        [HttpGet("profiles/progress"), Produces(typeof(ApiResponse<ProfileProgressResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<ProfileProgressResponseViewModel>> GetProfileProgress()
        {
            try
            {
                var result = await profileProgressService.Value.GetProgressAsync(User.UserId());

                return Ok<ProfileProgressResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : ToProgressViewModel(result.Data),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ProfileProgressResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        private static ProfileProgressResponseViewModel ToProgressViewModel(ProfileProgressDto data) => new()
        {
            Level = data.Level,
            Percent = data.Percent,
            NextLevel = data.NextLevel,
            NextLevelAtPercent = data.NextLevelAtPercent,
            Quests = data.Quests.Select(t => new ProfileQuestViewModel { Code = t.Code, Done = t.Done, Percent = t.Percent }),
            Points = data.Points,
            Rank = data.Rank,
            Practice = new()
            {
                Current = data.Practice.Current,
                Best = data.Practice.Best,
                PractisedToday = data.Practice.PractisedToday,
                LastSevenDays = data.Practice.LastSevenDays,
            },
            School = data.School is null ? null : new()
            {
                Id = data.School.Id,
                Name = data.School.Name,
                Reviews = data.School.Reviews,
                ReviewGoal = data.School.ReviewGoal,
                Photos = data.School.Photos,
                Students = data.School.Students,
                Teachers = data.School.Teachers,
                HelpersThisMonth = data.School.HelpersThisMonth,
                MyReview = data.School.MyReview,
                ReviewPoints = data.School.ReviewPoints,
                PhotoPoints = data.School.PhotoPoints,
                EditPoints = data.School.EditPoints,
            },
        };

        [HttpGet("profiles/list"), Produces(typeof(ApiResponse<ListDataSource<PublicProfileListResponseViewModel>>))]
        [Permission(policy: null)]
        [AllowAnonymous]
        [Display(Name = "Get Public Profiles list")]
        public async Task<IActionResult<ListDataSource<PublicProfileListResponseViewModel>>> GetPublicProfile([NotNull, FromQuery] PublicProfileListRequestViewModel request)
        {
            try
            {
                // Named, public teachers only - the same rule the sitemap uses (see TeacherDirectorySpecification).
                ISpecification<ApplicationUser> specification = new TeacherDirectorySpecification();

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
                        Handle = result.Data.Handle,
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

        /// <summary>
        /// Sets the caller's own user type (<c>Teacher</c> or <c>Student</c>) and the matching role, on this backend only. Replaced
        /// <c>legacy-auth/group</c> (removed 2026-11-23). See
        /// docs/business/identity-and-access.md, "User type".
        /// </summary>
        [HttpPut("profiles/role"), Produces(typeof(ApiResponse<Void>))]
        [Permission(policy: null)]
        public async Task<IActionResult<Void>> UpdateRole([NotNull] UpdateUserRoleRequestViewModel request)
        {
            try
            {
                var group = request.Role?.Trim().ToUpperInvariant() switch
                {
                    "TEACHER" => 5,
                    "STUDENT" => 6,
                    _ => 0,
                };
                var result = await identityService.Value.UpdateUserTypeAsync(User.UserId(), group);

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

        /// <summary>Emails the signed-in user a one-time code (10 minutes) to confirm deleting their account. Returns the masked address it went to.</summary>
        [HttpPost("profiles/delete-code"), Produces(typeof(ApiResponse<string>))]
        [Permission(policy: null)]
        [Display(Name = "Send Removing User Account Code")]
        public async Task<IActionResult<string>> SendDeleteAccountCode()
        {
            try
            {
                var result = await identityService.Value.SendDeleteAccountCodeAsync(User.UserId());

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

        /// <summary>Schedules the signed-in user's account for deletion in 7 days, confirmed by the code from profiles/delete-code.</summary>
        [HttpDelete("profiles"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Request Removing User Account")]
        public async Task<IActionResult<bool>> DeleteAccount([NotNull] DeleteAccountRequestViewModel request)
        {
            try
            {
                var result = await identityService.Value.InitializeDeletingAccountAsync(User.UserId(), request.Code);

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

        /// <summary>Cancels the signed-in user's pending deletion. The session alone is enough: keeping an account harms nobody.</summary>
        [HttpPatch("profiles/recover"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [Display(Name = "Cancel Removing User Account Request")]
        public async Task<IActionResult<bool>> RecoverAccount()
        {
            try
            {
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


        /// <summary>This backend's bearer token for a signed-in user, with the sign-in recorded.</summary>
        private async Task<IActionResult<GenerateTokenResponseViewModel>> IssueTokenAsync(long userId)
        {
            var result = await identityService.Value.GenerateUserTokenAsync(new()
            {
                UserId = userId,
                TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
            });

            _ = await identityService.Value.AddLoginHistoryAsync(new()
            {
                UserId = userId,
                IpAddress = HttpContext.GetClientIpAddress(),
                UserAgent = HttpContext.UserAgent(),
            });

            return Ok<GenerateTokenResponseViewModel>(new(result.Errors)
            {
                Data = result.Data is null ? null : new()
                {
                    Token = result.Data.Token,
                    ExpirationTime = result.Data.ExpirationTime,
                },
            });
        }

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<Task<ResultData<T>>> call)
        {
            try
            {
                var result = await call();
                return Ok<T>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<T>(new(new Error { Message = exc.Message }));
            }
        }
    }
}

