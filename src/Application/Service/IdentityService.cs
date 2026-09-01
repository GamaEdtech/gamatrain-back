namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Numerics;
    using System.Security.Claims;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;

    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Collections.Generic;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Mapping;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Experience;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Data.Dto.SiteMap;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;

    using NetTopologySuite.Geometries;

    using static GamaEdtech.Common.Core.Constants;

    using Error = Common.Data.Error;
    using Void = Common.Data.Void;

    public partial class IdentityService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<IdentityService>> localizer, Lazy<ILogger<IdentityService>> logger
            , Lazy<UserManager<ApplicationUser>> userManager, Lazy<IGenericFactory<IAuthenticationProvider, AuthenticationProvider>> genericFactory, Lazy<IApplicationSettingsService> applicationSettingsService
            , Lazy<SignInManager<ApplicationUser>> signInManager, Lazy<ICacheProvider> cacheProvider, Lazy<IConfiguration> configuration, Lazy<ICoreProvider> coreProvider, Lazy<IEmailService> emailService, Lazy<IFileService> fileService)
        : LocalizableServiceBase<IdentityService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IIdentityService, ITokenService, ISiteMapHandler
    {
        private const string RolesCacheKey = "Roles";

        public async Task<ResultData<ListDataSource<ApplicationUserDto>>> GetUsersAsync(ListRequestDto<ApplicationUser>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<ApplicationUser>().GetManyQueryable(requestDto?.Specification);

                var result = await query.FilterListAsync(requestDto?.PagingDto);

                var users = await result.List.Select(t => new ApplicationUserDto
                {
                    Id = t.Id,
                    Email = t.Email,
                    Enabled = t.Enabled,
                    PhoneNumber = t.PhoneNumber,
                    UserName = t.UserName,
                    RegistrationDate = t.RegistrationDate,
                    ReferralId = t.ReferralId,
                    FirstName = t.FirstName,
                    LastName = t.LastName,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new ListDataSource<ApplicationUserDto> { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<IEnumerable<ApplicationRoleDto>>> GetRolesAsync(ISpecification<ApplicationRoleDto>? specification = null)
        {
            try
            {
                var lst = await cacheProvider.Value.GetAsync<IEnumerable<ApplicationRoleDto>?>(RolesCacheKey, async () =>
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    return await uow.GetRepository<ApplicationRole>().GetManyQueryable().Select(t => new ApplicationRoleDto
                    {
                        Id = t.Id,
                        Name = t.Name!,
                    }).ToListAsync();
                });

                if (lst is not null && specification is not null)
                {
                    lst = lst.Where(specification.IsSatisfiedBy);
                }

                return new(OperationResult.Succeeded) { Data = lst };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<ApplicationUserDto>> GetUserAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => new ApplicationUserDto
                {
                    Id = t.Id,
                    Email = t.Email,
                    Enabled = t.Enabled,
                    PhoneNumber = t.PhoneNumber,
                    UserName = t.UserName,
                    RegistrationDate = t.RegistrationDate,
                    FirstName = t.FirstName,
                    LastName = t.LastName,
                    ReferralId = t.ReferralId,
                }).FirstOrDefaultAsync();

                return user is null
                    ? new(OperationResult.NotFound) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } }, }
                    : new(OperationResult.Succeeded)
                    {
                        Data = user,
                    };
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, nameof(GetUserAsync));
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<List<long>>> GetUserIdsAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var ids = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => t.Id).ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = ids,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, nameof(GetUserAsync));
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<List<string?>>> GetUsersEmailAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var emails = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => t.Email).ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = emails,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, nameof(GetUserAsync));
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<(long Id, string? FullName)?>> GetUserFullNameAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.FirstName,
                    t.LastName,
                }).FirstOrDefaultAsync();

                return new(data is null ? OperationResult.NotFound : OperationResult.Succeeded)
                {
                    Data = data is not null ? (data.Id, $"{data.FirstName} {data.LastName}") : null,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, nameof(GetUserAsync));
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Point?>> GetUserCoordinateAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => t.City != null ? t.City.Coordinates : null).FirstOrDefaultAsync();

                return new(data is null ? OperationResult.NotFound : OperationResult.Succeeded)
                {
                    Data = data,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, nameof(GetUserAsync));
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<ICollection<string>>> GetUserRolesAsync([NotNull] long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var roles = await uow.GetRepository<ApplicationUserRole>().GetManyQueryable(t => t.UserId == userId).Select(t => t.Role!.Name!).ToListAsync();
                return new(OperationResult.Succeeded) { Data = roles };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> UserIsInRoleAsync([NotNull] long userId, [NotNull] string role)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var normalizedRoleName = role.ToUpperInvariant();
                var result = await uow.GetRepository<ApplicationUserRole>().AnyAsync(t => t.UserId == userId && t.Role!.NormalizedName == normalizedRoleName);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<AuthenticationResponseDto>> AuthenticateAsync([NotNull] AuthenticationRequestDto requestDto)
        {
            try
            {
                return await genericFactory.Value.GetProvider(requestDto.AuthenticationProvider)!.AuthenticateAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> RegisterAsync([NotNull] RegistrationRequestDto requestDto)
        {
            try
            {
                var user = new ApplicationUser
                {
                    UserName = requestDto.Username,
                    Email = requestDto.Email,
                    RegistrationDate = DateTime.UtcNow,
                    Enabled = true,
                    ProfileVisibility = ProfileVisibility.Private,
                    FirstName = requestDto.FirstName,
                    LastName = requestDto.LastName,
                };
                var identityResult = await userManager.Value.CreateAsync(user, requestDto.Password);
                return identityResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = true }
                    : new(OperationResult.NotValid) { Data = false, Errors = MapUserManagerErrors(identityResult) };
            }
            catch (UniqueConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["DuplicateUsername"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task SendRegistrationEmailAsync([NotNull] RegistrationEmailRequestDto requestDto)
        {
            var name = requestDto.FirstName + " " + requestDto.LastName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Dear User";
            }
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.RegistrationEmailTemplate))).Data;
            template = template?
                .Replace("[RECEIVER_NAME]", name, StringComparison.OrdinalIgnoreCase);
            _ = await emailService.Value.SendEmailAsync(new()
            {
                Subject = "Gamatrain Registration",
                Body = template!,
                EmailAddresses = [requestDto.Email],
                From = emailService.Value.GetNoReplyEmail(),
            });
        }

        public async Task<ResultData<SignInResponseDto>> SignInAsync([NotNull] SignInRequestDto requestDto)
        {
            try
            {
                var timeZoneId = await GetTimeZoneIdAsync(requestDto.User.Id);
                List<Claim> claims = [
                    new Claim(ClaimTypes.Email, requestDto.User.Email ?? string.Empty),
                    new Claim(ClaimTypes.MobilePhone, requestDto.User.PhoneNumber ?? string.Empty),
                    new Claim(ClaimTypes.System, GenerateDeviceHash(HttpContextAccessor.Value.HttpContext) ?? string.Empty),
                    new Claim(TimeZoneIdClaim, timeZoneId),
                ];
                var user = requestDto.User.AdaptData<ApplicationUserDto, ApplicationUser>();
                var roles = await signInManager.Value.UserManager.GetRolesAsync(user);
                if (roles is not null)
                {
                    for (var i = 0; i < roles.Count; i++)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, roles[i]));
                    }
                }

                await signInManager.Value.SignInWithClaimsAsync(user, requestDto.RememberMe, claims);

                return new(OperationResult.Succeeded)
                {
                    Data = new SignInResponseDto
                    {
                        Roles = (await GetUserRolesAsync(user.Id)).Data,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Void>> SignOutAsync()
        {
            try
            {
                await signInManager.Value.SignOutAsync();

                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> CreateUserAsync([NotNull] CreateUserRequestDto requestDto)
        {
            try
            {
                var (avatarId, errors) = await SaveFileAsync(requestDto.Avatar);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                var user = new ApplicationUser
                {
                    UserName = requestDto.Username,
                    Email = requestDto.Email,
                    PhoneNumber = requestDto.PhoneNumber,
                    RegistrationDate = DateTime.UtcNow,
                    Enabled = true,
                    EmailConfirmed = true,
                    PhoneNumberConfirmed = true,
                    AvatarId = avatarId,
                    FirstName = requestDto.FirstName,
                    LastName = requestDto.LastName,
                };
                var identityResult = await userManager.Value.CreateAsync(user, requestDto.Password);
                return !identityResult.Succeeded
                    ? new(OperationResult.NotValid) { Data = false, Errors = MapUserManagerErrors(identityResult) }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (UniqueConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["DuplicateUsername"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> UpdateUserAsync([NotNull] UpdateUserRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.Id.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Data = false };
                }

                var (avatarId, errors) = await SaveFileAsync(requestDto.Avatar);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                user.Email = requestDto.Email;
                user.PhoneNumber = requestDto.PhoneNumber;
                user.UserName = requestDto.Username;
                user.FirstName = requestDto.FirstName;
                user.LastName = requestDto.LastName;
                user.AvatarId = avatarId;

                var updateUserResult = await userManager.Value.UpdateAsync(user);
                return updateUserResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = true }
                    : new(OperationResult.NotValid) { Data = false, Errors = MapUserManagerErrors(updateUserResult) };
            }
            catch (UniqueConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["DuplicateUsername"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> ToggleUserAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var user = await uow.GetRepository<ApplicationUser>().GetAsync(specification);
                if (user is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } },
                    };
                }

                user.Enabled = !user.Enabled;

                _ = uow.GetRepository<ApplicationUser>().Update(user);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> RemoveUserAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();
                var user = await repository.GetAsync(specification);
                if (user is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } },
                    };
                }

                repository.Remove(user);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserCantBeRemoved"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<string?>> GetUserTokenAsync([NotNull] GetUserTokenRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } },
                    };
                }

                var token = await userManager.Value.GetAuthenticationTokenAsync(user, requestDto.TokenProvider, requestDto.Purpose);
                return new(OperationResult.Succeeded) { Data = string.IsNullOrEmpty(token) ? null : $"{requestDto.UserId}{DelimiterAlternate}{token}" };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message } } };
            }
        }

        public async Task<ResultData<GenerateUserTokenResponseDto>> GenerateUserTokenAsync([NotNull] GenerateUserTokenRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } },
                    };
                }

                var token = await userManager.Value.GenerateUserTokenAsync(user, requestDto.TokenProvider, requestDto.Purpose);
                var setTokenResult = await userManager.Value.SetAuthenticationTokenAsync(user, requestDto.TokenProvider, requestDto.Purpose, token);

                return setTokenResult.Succeeded
                    ? new(OperationResult.Succeeded)
                    {
                        Data = new GenerateUserTokenResponseDto
                        {
                            UserId = requestDto.UserId,
                            Token = $"{requestDto.UserId}{DelimiterAlternate}{token}",
                            ExpirationTime = DateTimeOffset.UtcNow.Add(ApiDataProtectorTokenProviderOptions.GetTokenLifespan(configuration.Value)),
                        }
                    }
                    : new(OperationResult.NotValid) { Errors = MapUserManagerErrors(setTokenResult) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<VerifyTokenResponse?> VerifyTokenAsync([NotNull] VerifyTokenRequest request)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(request.UserId!);
                if (user is null)
                {
                    return null;
                }

                var validationResult = ValidateUser<VerifyTokenResponse>(user);
                if (validationResult.OperationResult is not OperationResult.Succeeded)
                {
                    return null;
                }

                var verifiyTokenResult = await userManager.Value.VerifyUserTokenAsync(user!, request.TokenProvider!, request.Purpose!, request.Token!);
                return !verifiyTokenResult
                    ? null
                    : new VerifyTokenResponse { Claims = await BuildUserClaimsAsync(user!, request.UserId ?? string.Empty) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        public async Task<VerifyTokenResponse?> VerifyLegacyTokenAsync([NotNull] string token)
        {
            try
            {
                var validation = await ValidateLegacyJwtAsync(token);
                if (!validation.IsValid)
                {
                    return null;
                }

                _ = validation.Claims.TryGetValue("user_id", out var userIdClaim);
                var coreId = userIdClaim?.ToString().ValueOf<long?>();
                if (coreId is null)
                {
                    return null;
                }

                var user = await userManager.Value.Users.FirstOrDefaultAsync(t => t.CoreId == coreId);
                if (user is null)
                {
                    return null;
                }

                var validationResult = ValidateUser<VerifyTokenResponse>(user);
                return validationResult.OperationResult is not OperationResult.Succeeded
                    ? null
                    : new VerifyTokenResponse { Claims = await BuildUserClaimsAsync(user, user.Id.ToString(CultureInfo.InvariantCulture)) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        private async Task<List<Claim>> BuildUserClaimsAsync(ApplicationUser user, string userIdClaim)
        {
            var timeZoneId = await GetTimeZoneIdAsync(user.Id);
            List<Claim> claims = [
                new Claim(ClaimTypes.NameIdentifier, userIdClaim),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty),
                new Claim(TimeZoneIdClaim, timeZoneId ?? string.Empty),
            ];

            var roles = await GetUserRolesAsync(user.Id);
            if (roles.Data is not null)
            {
                foreach (var item in roles.Data)
                {
                    claims.Add(new Claim(ClaimTypes.Role, item));
                }
            }

            return claims;
        }

        /// <summary>
        /// Validates a gama-api (legacy) JWT's issuer/audience/expiry AND its HS256 signature against the shared
        /// Core:JwtSigningSecret. Used by both the legacy-auth-bridge (SyncLegacyAuthAsync, VerifyLegacyTokenAsync)
        /// and the pre-existing tokens/old bridge (GenerateTokenByCoreTokenAsync) - all three accept a token
        /// claiming to belong to some CoreId, so all three must cryptographically verify it actually came from
        /// gama-api, not just structurally resemble one of their tokens.
        /// </summary>
        private async Task<TokenValidationResult> ValidateLegacyJwtAsync(string? token)
        {
            var endpoint = configuration.Value.GetValue<string>("Core:Url");
            var secret = configuration.Value.GetValue<string>("Core:JwtSigningSecret") ?? string.Empty;
            return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = endpoint,
                RequireExpirationTime = true,
                ValidateActor = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidAudience = endpoint,
            });
        }

        public async Task<ResultData<bool>> RemoveUserTokenAsync([NotNull] RemoveUserTokenRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"], } } };
                }

                var removeTokenResult = await userManager.Value.RemoveAuthenticationTokenAsync(user, requestDto.TokenProvider, requestDto.Purpose);
                return removeTokenResult.Succeeded ? new(OperationResult.Succeeded) { Data = true } : new(OperationResult.Failed) { Errors = MapUserManagerErrors(removeTokenResult), };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> ChangePasswordAsync([NotNull] ChangePasswordRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(HttpContextAccessor.Value.HttpContext?.User.UserId<string>()!);
                if (user is null)
                {
                    return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"], } } };
                }
                var changePasswordResult = await userManager.Value.ChangePasswordAsync(user, requestDto.CurrentPassword, requestDto.NewPassword);
                return changePasswordResult.Succeeded
                        ? new(OperationResult.Succeeded) { Data = true }
                        : new(OperationResult.NotValid) { Data = false, Errors = MapUserManagerErrors(changePasswordResult) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> ResetPasswordAsync([NotNull] ResetPasswordRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"], } } };
                }
                var passwordResetToken = await userManager.Value.GeneratePasswordResetTokenAsync(user);
                var resetPasswordResult = await userManager.Value.ResetPasswordAsync(user, passwordResetToken, requestDto.NewPassword);
                return resetPasswordResult.Succeeded
                        ? new(OperationResult.Succeeded) { Data = true }
                        : new(OperationResult.NotValid) { Data = false, Errors = MapUserManagerErrors(resetPasswordResult) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task ValidatePrincipalAsync([NotNull] CookieValidatePrincipalContext context)
        {
            var systemClaim = context.Principal?.FindFirstValue(ClaimTypes.System);
            if (string.IsNullOrEmpty(systemClaim))
            {
                await handleUnauthorizedRequestAsync();
                return;
            }

            var hash = GenerateDeviceHash(context.HttpContext);
            if (!systemClaim.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                await handleUnauthorizedRequestAsync();
            }

            var userId = context.Principal.UserId();
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var currentSecurityStamp = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId).Select(t => t.SecurityStamp).FirstOrDefaultAsync();
            var securityStampClaim = context.Principal?.FindFirstValue(userManager.Value.Options.ClaimsIdentity.SecurityStampClaimType);
            if (currentSecurityStamp != securityStampClaim)
            {
                await handleUnauthorizedRequestAsync();
            }

            async Task handleUnauthorizedRequestAsync()
            {
                context.RejectPrincipal();
                var identityService = context.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
                _ = await identityService.SignOutAsync();
            }
        }

        public async Task<ResultData<UserPermissionsResponseDto>> GetUserPermissionsAsync([NotNull] UserPermissionsRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<ApplicationUser>()
                    .GetManyQueryable(new IdEqualsSpecification<ApplicationUser, long>(requestDto.UserId))
                    .Select(t => new
                    {
                        Claims = t.UserClaims!.Select(u => new { u.ClaimType, u.ClaimValue }).ToList(),
                        Roles = t.UserRoles!.Select(u => u.Role!.Name!).ToList(),
                    }).FirstOrDefaultAsync();

                return data is null
                    ? new(OperationResult.NotFound) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] } }, }
                    : new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Permissions = data.Claims.Where(t => t.ClaimType == PermissionConstants.PermissionPolicy).Select(t => t.ClaimValue),
                            SystemClaims = data.Claims.Where(t => t.ClaimType == PermissionConstants.SystemClaim)
                                .Select(t => t.ClaimValue!).ListToFlagsEnum<SystemClaim>(),
                            Roles = data.Roles.ListToFlagsEnum<Role>(),
                        },
                    };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Void>> UpdateUserPermissionsAsync([NotNull] UpdateUserPermissionsRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"], } } };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var userRolesRepository = uow.GetRepository<ApplicationUserRole>();
                var userRoles = await userRolesRepository.GetManyQueryable(new UserIdEqualsSpecification<ApplicationUserRole, long>(requestDto.UserId))
                    .Select(t => t.Role!.Name!).ToListAsync();

                if (userRoles.Exists(t => t.Equals(nameof(Role.Admin), StringComparison.OrdinalIgnoreCase)) && requestDto.Roles?.ExistInFlags(Role.Admin) != true)
                {
                    var anotherAdminExists = await userRolesRepository.AnyAsync(t => t.UserId != requestDto.UserId && t.Role!.Name == nameof(Role.Admin));
                    if (!anotherAdminExists)
                    {
                        return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["LastAdminCantBeRemoved"] } } };
                    }
                }

                var forceLogout = false;

                #region Roles

                var requestRoles = requestDto.Roles?.GetNames() ?? [];
                var newRoles = requestRoles.Except(userRoles);
                var removedRoles = userRoles.Except(requestRoles);

                if (removedRoles.Any())
                {
                    forceLogout = true;
                    _ = await userManager.Value.RemoveFromRolesAsync(user, removedRoles);
                }

                if (newRoles.Any())
                {
                    forceLogout = true;
                    _ = await userManager.Value.AddToRolesAsync(user, newRoles);
                }

                #endregion

                var repository = uow.GetRepository<ApplicationUserClaim, int>();

                var specification = new UserIdEqualsSpecification<ApplicationUserClaim, long>(requestDto.UserId)
                    .And(new ClaimTypeEqualsSpecification(PermissionConstants.PermissionPolicy)
                        .Or(new ClaimTypeEqualsSpecification(PermissionConstants.SystemClaim))
                    );
                var claims = await repository.GetManyQueryable(specification)
                    .Select(t => new { t.ClaimType, t.ClaimValue }).ToListAsync();

                #region Permissions

                var newPermissions = requestDto.Permissions.Except(claims.Where(t => t.ClaimType == PermissionConstants.PermissionPolicy).Select(t => t.ClaimValue));
                var removedPermissions = claims.Where(t => t.ClaimType == PermissionConstants.PermissionPolicy && !requestDto.Permissions.Contains(t.ClaimValue)).Select(t => t.ClaimValue);

                if (newPermissions.Any())
                {
                    forceLogout = true;
                    foreach (var item in newPermissions)
                    {
                        repository.Add(new ApplicationUserClaim { UserId = requestDto.UserId, ClaimType = PermissionConstants.PermissionPolicy, ClaimValue = item });
                    }
                    _ = await uow.SaveChangesAsync();
                }

                if (removedPermissions.Any())
                {
                    forceLogout = true;
                    _ = await repository.GetManyQueryable(t => t.UserId == requestDto.UserId && t.ClaimType == PermissionConstants.PermissionPolicy && removedPermissions.Contains(t.ClaimValue))
                        .ExecuteDeleteAsync();
                }

                #endregion

                #region System Claims

                var requestClaims = requestDto.SystemClaims?.GetNames() ?? [];
                var newClaims = requestClaims.Except(claims.Where(t => t.ClaimType == PermissionConstants.SystemClaim).Select(t => t.ClaimValue));
                var removedClaims = claims.Where(t => t.ClaimType == PermissionConstants.SystemClaim && !requestClaims.Contains(t.ClaimValue)).Select(t => t.ClaimValue);

                if (newClaims.Any())
                {
                    forceLogout = true;
                    foreach (var item in newClaims)
                    {
                        repository.Add(new ApplicationUserClaim { UserId = requestDto.UserId, ClaimType = PermissionConstants.SystemClaim, ClaimValue = item });
                    }
                    _ = await uow.SaveChangesAsync();
                }

                if (removedClaims.Any())
                {
                    forceLogout = true;
                    _ = await repository.GetManyQueryable(t => t.UserId == requestDto.UserId && t.ClaimType == PermissionConstants.SystemClaim && removedClaims.Contains(t.ClaimValue))
                        .ExecuteDeleteAsync();
                }

                #endregion

                if (forceLogout)
                {
                    _ = await userManager.Value.UpdateSecurityStampAsync(user);
                }

                return new(OperationResult.Succeeded);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed)
                {
                    Errors = new[] { new Error { Message = exc.Message }, }
                };
            }
        }

        public async Task<ResultData<ProfileSettingsDto>> GetProfileSettingsAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<ApplicationUser>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.UserName,
                    t.FirstName,
                    t.LastName,
                    t.SchoolId,
                    t.CityId,
                    StateId = t.City != null ? t.City.ParentId : null,
                    CountryId = t.City != null && t.City.Parent != null ? t.City.Parent.ParentId : null,
                    t.ReferralId,
                    t.Gender,
                    t.Board,
                    t.Grade,
                    t.AvatarId,
                    t.Group,
                    t.CoreId,
                    t.WalletId,
                    t.ProfileUpdated,
                    t.ProfileVisibility,
                    Roles = t.UserRoles != null ? t.UserRoles.Select(u => u.Role!.Name!) : null,
                    t.Biography,
                    t.Skills,
                    t.CurrentStatusSentence,
                    t.Handle,
                    t.OrphanDate,
                    Experiences = t.Experiences == null ? null : t.Experiences.Select(e => new
                    {
                        e.Id,
                        e.SchoolId,
                        e.School.Name,
                        e.Description,
                        e.StartDate,
                        e.EndDate,
                    }),
                }).FirstOrDefaultAsync();
                if (data is null)
                {
                    return new(OperationResult.Failed) { Errors = new[] { new Error { Message = "User not found." } } };
                }

                var skills = data.Skills?.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
                var experiences = data.Experiences?.Select(t => new ExperienceDto
                {
                    Id = t.Id,
                    SchoolId = t.SchoolId,
                    SchoolTitle = t.Name,
                    Description = t.Description,
                    StartDate = t.StartDate,
                    EndDate = t.EndDate,
                });

                var handle = string.IsNullOrEmpty(data.Handle)
                    ? $"{data.Id}-{data.FirstName}-{data.LastName}".ToLowerInvariant()
                    : data.Handle;

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        UserName = data.UserName,
                        FirstName = data.FirstName,
                        LastName = data.LastName,
                        SchoolId = data.SchoolId,
                        CityId = data.CityId,
                        StateId = data.StateId,
                        CountryId = data.CountryId,
                        ReferralId = data.ReferralId,
                        Gender = data.Gender,
                        Board = data.Board,
                        Grade = data.Grade,
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = data.AvatarId, ContainerType = ContainerType.User }),
                        Group = data.Group,
                        CoreId = data.CoreId,
                        WalletId = data.WalletId,
                        ProfileUpdated = data.ProfileUpdated,
                        Roles = data.Roles?.ListToFlagsEnum<Role>(),
                        ProfileVisibility = data.ProfileVisibility,
                        Biography = data.Biography,
                        Skills = skills,
                        CurrentStatusSentence = data.CurrentStatusSentence,
                        Experiences = experiences,
                        UserRateLevel = UserRateLevel.Calculate(data.AvatarId, data.FirstName, data.LastName, data.CurrentStatusSentence, data.Biography, skills, experiences?.Select(t => t.Id)),
                        Handle = handle,
                        OrphanDate = data.OrphanDate,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return new(OperationResult.Failed)
                {
                    Errors = new[] { new Error { Message = exc.Message } }
                };
            }
        }

        public async Task<ResultData<bool>> ManageProfileSettingsAsync([NotNull] ManageProfileSettingsRequestDto requestDto)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(requestDto.UserId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = new[] { new Error { Message = "User not found." } }
                    };
                }

                var valid = await ValidateHandleAsync(new()
                {
                    Handle = requestDto.Handle,
                    UserId = requestDto.UserId,
                });
                if (valid.OperationResult is not OperationResult.Succeeded)
                {
                    return new(valid.OperationResult) { Errors = valid.Errors };
                }

                var (avatarId, errors) = await SaveFileAsync(requestDto.Avatar);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                user.CityId = requestDto.CityId ?? user.CityId;
                user.SchoolId = requestDto.SchoolId ?? user.SchoolId;
                user.FirstName = !string.IsNullOrEmpty(requestDto.FirstName) ? requestDto.FirstName : user.FirstName;
                user.LastName = !string.IsNullOrEmpty(requestDto.LastName) ? requestDto.LastName : user.LastName;
                user.AvatarId = !string.IsNullOrEmpty(avatarId) ? avatarId : user.AvatarId;
                user.Gender = requestDto.Gender ?? user.Gender;
                user.Board = requestDto.Board ?? user.Board;
                user.Grade = requestDto.Grade ?? user.Grade;
                user.Group = requestDto.Group ?? user.Group;
                user.CoreId = requestDto.CoreId ?? user.CoreId;
                user.WalletId = requestDto.WalletId ?? user.WalletId;
                user.ProfileVisibility = requestDto.ProfileVisibility ?? user.ProfileVisibility;
                user.Biography = requestDto.Biography ?? user.Biography;
                user.Skills = requestDto.Skills?.Any() == true ? string.Join(Delimiter, requestDto.Skills ?? []) : user.Skills;
                user.CurrentStatusSentence = requestDto.CurrentStatusSentence ?? user.CurrentStatusSentence;
                user.Handle = valid.Data ?? user.Handle;
                user.ProfileUpdated = true;

                var updateResult = await userManager.Value.UpdateAsync(user);

                return updateResult.Succeeded
                    ? new ResultData<bool>(OperationResult.Succeeded) { Data = true }
                    : new ResultData<bool>(OperationResult.NotValid)
                    {
                        Data = false,
                        Errors = updateResult.Errors.Select(t => new Error { Message = t.Description }).ToArray()
                    };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed)
                {
                    Errors = new[] { new Error { Message = exc.Message } }
                };
            }
        }

        public async Task<ResultData<bool>> HasClaimAsync(long userId, SystemClaim claims)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var userClaimsRepository = uow.GetRepository<ApplicationUserClaim, int>();
                var names = claims.GetNames();
                var exists = await userClaimsRepository.AnyAsync(t => t.UserId == userId && t.ClaimType == PermissionConstants.SystemClaim && names.Contains(t.ClaimValue));

                return new(OperationResult.Succeeded)
                {
                    Data = exists,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<string>> GenerateReferralUserAsync()
        {
            try
            {
                var userId = HttpContextAccessor.Value.HttpContext?.User.UserId();

                if (!userId.HasValue)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = new[] { new Error { Message = Localizer.Value["AuthenticationError"] } },
                    };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var userRepo = uow.GetRepository<ApplicationUser>();

                var user = await userRepo.GetAsync(userId.Value);

                if (user == null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"].Value } },
                    };
                }

                if (user.ReferralId != null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = new[] { new Error { Message = Localizer.Value["AlreadyHaveReferralId"].Value } },
                    };
                }

                var referralId = "";
                bool exists;
                var tryCount = 5;

                do
                {
                    if (tryCount <= 0)
                    {
                        return new(OperationResult.Failed)
                        {
                            Errors = [new() { Message = Localizer.Value["ReferralIdGenerationFailed"] }]
                        };
                    }
                    referralId = GenerateReferralId(userId ?? 0);
                    exists = await userRepo.AnyAsync(u => u.ReferralId == referralId);
                    tryCount--;
                }
                while (exists);


                user.ReferralId = referralId;
                _ = userRepo.Update(user);

                _ = await uow.SaveChangesAsync();


                return new(OperationResult.Succeeded) { Data = referralId };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = Localizer.Value["ReferralUserConstraintError"] }]
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed)
                {
                    Errors = [new() { Message = exc.Message }]
                };
            }
        }

        public static string GenerateReferralId(long userId)
        {
            var inputBytes = Encoding.UTF8.GetBytes(
                $"{userId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid()}"
            );

            var hashBytes = SHA256.HashData(inputBytes);
            var base62Hash = Base62Encode(hashBytes);

            // Take the first 10 characters
            var id = base62Hash[..10].ToCharArray();

            // Ensure at least 1 uppercase
            if (!id.Any(char.IsUpper))
            {
                id[RandomNumberGenerator.GetInt32(id.Length)] = (char)('A' + RandomNumberGenerator.GetInt32(26));
            }

            // Ensure at least 1 lowercase
            if (!id.Any(char.IsLower))
            {
                id[RandomNumberGenerator.GetInt32(id.Length)] = (char)('a' + RandomNumberGenerator.GetInt32(26));
            }

            // Ensure at least 1 digit
            if (!id.Any(char.IsDigit))
            {
                id[RandomNumberGenerator.GetInt32(id.Length)] = (char)('0' + RandomNumberGenerator.GetInt32(10));

            }

            return new string(id);

            static string Base62Encode(byte[] bytes)
            {
                const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
                var sb = new StringBuilder();

                // Convert to a big integer
                var value = new BigInteger(bytes.Concat(new byte[] { 0 }).ToArray());

                while (value > 0)
                {
                    var remainder = (int)(value % 62);
                    _ = sb.Insert(0, chars[remainder]);
                    value /= 62;
                }

                return sb.ToString();
            }
        }

        public async Task<ResultData<List<UserPointsDto>>> GetTop100UsersAsync(Top100UsersRequestDto? requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var adminRole = nameof(Role.Admin).ToUpperInvariant();
                var lst = uow.GetRepository<ApplicationUser>().GetManyQueryable(t => !t.UserRoles!.Any(r => r.Role!.NormalizedName == adminRole));

                if (requestDto is not null)
                {
                    if (requestDto.Board.HasValue)
                    {
                        lst = lst.Where(t => t.Board == requestDto.Board.Value);
                    }

                    if (requestDto.Grade.HasValue)
                    {
                        lst = lst.Where(t => t.Grade == requestDto.Grade.Value);
                    }

                    if (requestDto.CityId.HasValue)
                    {
                        lst = lst.Where(t => t.CityId == requestDto.CityId.Value);
                    }

                    if (requestDto.SchoolId.HasValue)
                    {
                        lst = lst.Where(t => t.SchoolId == requestDto.SchoolId.Value);
                    }

                    if (requestDto.RegistrationDateStart.HasValue)
                    {
                        lst = lst.Where(t => t.RegistrationDate >= requestDto.RegistrationDateStart.Value);
                    }

                    if (requestDto.RegistrationDateEnd.HasValue)
                    {
                        lst = lst.Where(t => t.RegistrationDate <= requestDto.RegistrationDateEnd.Value);
                    }

                    if (requestDto.StateId.HasValue)
                    {
                        lst = lst.Where(t => t.City != null && t.City.ParentId == requestDto.StateId.Value);
                    }

                    if (requestDto.CountryId.HasValue)
                    {
                        lst = lst.Where(t => t.City != null && t.City.Parent != null && t.City.Parent.ParentId == requestDto.CountryId.Value);
                    }
                }

                var data = await lst.Select(t => new
                {
                    t.FirstName,
                    t.LastName,
                    t.CurrentBalance,
                    t.Id,
                    t.AvatarId,
                }).OrderByDescending(t => t.CurrentBalance).Take(100).ToListAsync();
                if (data is null)
                {
                    return new(OperationResult.Succeeded) { };
                }


                List<UserPointsDto> result = new(data.Count);
                for (var i = 0; i < data.Count; i++)
                {
                    result.Add(new()
                    {
                        Name = $"{data[i].FirstName} {data[i].LastName}",
                        UserId = data[i].Id,
                        Points = data[i].CurrentBalance,
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = data[i].AvatarId, ContainerType = ContainerType.User, }),
                    });
                }

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<GenerateUserTokenResponseDto>> GenerateTokenByCoreTokenAsync([NotNull] GenerateTokenByCoreTokenRequestDto requestDto)
        {
            try
            {
                var data = await ValidateLegacyJwtAsync(requestDto.Token);
                if (!data.IsValid)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = [new Error { Message = Localizer.Value["InvalidToken"] }],
                    };
                }

                var response = await coreProvider.Value.GetUserInformationAsync(new()
                {
                    Token = requestDto.Token,
                });
                if (response.OperationResult is not OperationResult.Succeeded)
                {
                    return new(response.OperationResult) { Errors = response.Errors };
                }

                if (response.Data is null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = [new Error { Message = Localizer.Value["InvalidToken"] }],
                    };
                }

                _ = data.Claims.TryGetValue("identity", out var email);

                var user = await userManager.Value.FindByEmailAsync(email?.ToString()!);
                if (user is null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = [new Error { Message = Localizer.Value["InvalidToken"] }],
                    };
                }

                user.Group = response.Data.Group;
                user.CoreId = response.Data.CoreId;

                if (!user.ProfileUpdated)
                {
                    if (response.Data.Avatar is not null)
                    {
                        using var stream = new MemoryStream(response.Data.Avatar.Content);
                        var file = new FormFile(stream, 0, response.Data.Avatar.Content.LongLength, "Avatar", response.Data.Avatar.Name)
                        {
                            Headers = new HeaderDictionary(),
                            ContentType = $"image/{Path.GetExtension(response.Data.Avatar.Name).Trim('.')}",
                            ContentDisposition = new System.Net.Mime.ContentDisposition
                            {
                                FileName = response.Data.Avatar.Name,
                            }.ToString(),
                        };
                        var avatarResult = await fileService.Value.CreateFileAsync(new()
                        {
                            ContainerType = ContainerType.User,
                            File = file,
                        });
                        user.AvatarId = avatarResult.Data;
                    }
                    user.FirstName = response.Data.FirstName;
                    user.LastName = response.Data.LastName;
                    user.Gender = response.Data.Gender;
                    user.Grade = response.Data.Grade;
                    user.PhoneNumber = response.Data.PhoneNumber;
                }
                _ = await userManager.Value.UpdateAsync(user);

                return await GenerateUserTokenAsync(new GenerateUserTokenRequestDto
                {
                    UserId = user.Id,
                    TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                    Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<LegacyBridgeTokenResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto)
        {
            try
            {
                var authResult = await coreProvider.Value.LegacyLoginAsync(requestDto);
                return authResult switch
                {
                    { OperationResult: not OperationResult.Succeeded } or { Data: null }
                        => new(authResult.OperationResult) { Errors = authResult.Errors },
                    // gama-api wants an OTP step-up (see LegacyAuthResponseDto.Type) instead of a token - relay
                    // that as-is, there's nothing to sync yet.
                    { Data.Token: null }
                        => new(OperationResult.Succeeded) { Data = new() { Type = authResult.Data.Type } },
                    _ => await SyncLegacyAuthAsync(authResult.Data, requestDto.Password),
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<LegacyBridgeTokenResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto)
        {
            try
            {
                var authResult = await coreProvider.Value.LegacyGoogleAuthAsync(requestDto);
                return authResult.OperationResult is not OperationResult.Succeeded || authResult.Data is null
                    ? new(authResult.OperationResult) { Errors = authResult.Errors }
                    : await SyncLegacyAuthAsync(authResult.Data, password: null);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto)
        {
            try
            {
                return await coreProvider.Value.LegacyRegisterAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto)
        {
            try
            {
                return await coreProvider.Value.LegacyRecoveryAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Void>> LegacyLogoutAsync([NotNull] string token)
        {
            try
            {
                return await coreProvider.Value.LegacyLogoutAsync(new() { Token = token });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Void>> LegacyUpdateGroupAsync(long userId, [NotNull] string token, int group)
        {
            try
            {
                var result = await coreProvider.Value.LegacyUpdateGroupAsync(new() { Token = token, Group = group });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return result;
                }

                var user = await userManager.Value.FindByIdAsync(userId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] }, } };
                }

                // gama-api already accepted the change (result above succeeded) - this side only needs to catch
                // up. A failure from here on out must not be reported as if the whole operation failed; gama-api
                // is the source of truth and it already has the new value.
                user.Group = group;
                _ = await userManager.Value.UpdateAsync(user);
                _ = await SyncRoleFromGroupAsync(user, group);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<DashboardResponseDto>> GetDashboardAsync(long userId, string? token)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString());
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = new[] { new Error { Message = Localizer.Value["UserNotFound"] }, } };
                }

                // Same shape check TokenAuthenticationHandler.HandleAuthenticateAsync uses to tell a local
                // opaque `{userId}|{token}` token (Constants.DelimiterAlternate = "|") apart from a raw
                // gama-api JWT (see docs/api/authentication.md) - a native/local-token account's token is
                // never forwardable to gama-api at all, so there's nothing to even attempt here. Bug fixed
                // 2026-09-01: this used to only check for a *missing* token (string.IsNullOrEmpty), which let
                // a local-opaque token straight through to CoreProvider.GetDashboardAsync - gama-api correctly
                // rejected it as garbage with 401/403, and that then got misread as "a real legacy session was
                // revoked" (LegacyAuthRejected), hard-failing this endpoint for every native-account caller.
                var isLegacyJwtShaped = !string.IsNullOrEmpty(token)
                    && token.Split(Constants.DelimiterAlternate, 2, StringSplitOptions.RemoveEmptyEntries).Length != 2;

                if (!isLegacyJwtShaped)
                {
                    return new(OperationResult.Succeeded) { Data = new() { LegacyDataAvailable = false } };
                }

                var result = await coreProvider.Value.GetDashboardAsync(new() { Token = token!, Group = user.Group });

                // gama-api being unreachable/erroring never fails this endpoint - it degrades to
                // LegacyDataAvailable = false so the frontend can render an empty state for those widgets.
                // See DashboardResponseDto.LegacyDataAvailable and docs/business/identity-and-access.md.
                // result.Data passes straight through on Succeeded, which also covers
                // DashboardResponseDto.LegacyAuthRejected - CoreProvider.GetDashboardAsync deliberately
                // returns Succeeded (not Failed) for that case too, since IdentitiesController.GetDashboard
                // needs to see it to propagate a real HTTP 401 - see that DTO property's doc comment.
                return new(OperationResult.Succeeded)
                {
                    Data = result.OperationResult is OperationResult.Succeeded && result.Data is not null
                        ? result.Data
                        : new() { LegacyDataAvailable = false },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        /// <summary>
        /// Shared by LegacyLoginAsync/LegacyGoogleAuthAsync (the only gama-api flows that return a token): decodes
        /// the legacy JWT to get CoreId/identity (same signature-skipping validation as GenerateTokenByCoreTokenAsync),
        /// finds the local user by CoreId, falling back to email/phone so a pre-existing native account gets linked
        /// instead of duplicated, creates one if none matches. No gamatrain-back token is minted here - the raw
        /// gama-api token is handed back to the frontend as-is and TokenAuthenticationHandler/VerifyLegacyTokenAsync
        /// resolves it straight to this same local user on every later request, so the frontend only ever holds one
        /// token and gama-api never has to change anything to keep validating it.
        /// </summary>
        private async Task<ResultData<LegacyBridgeTokenResponseDto>> SyncLegacyAuthAsync(LegacyAuthResponseDto authData, string? password)
        {
            var validation = await ValidateLegacyJwtAsync(authData.Token);
            if (!validation.IsValid)
            {
                return new(OperationResult.Failed) { Errors = [new Error { Message = Localizer.Value["InvalidToken"] }] };
            }

            _ = validation.Claims.TryGetValue("user_id", out var userIdClaim);
            _ = validation.Claims.TryGetValue("identity", out var identityClaim);
            var coreId = userIdClaim?.ToString().ValueOf<long?>();
            var identity = identityClaim?.ToString() ?? authData.Email ?? authData.PhoneNumber;
            if (coreId is null || string.IsNullOrEmpty(identity))
            {
                return new(OperationResult.Failed) { Errors = [new Error { Message = Localizer.Value["InvalidToken"] }] };
            }

            var user = await userManager.Value.Users.FirstOrDefaultAsync(t => t.CoreId == coreId);
            if (user is null && !string.IsNullOrEmpty(authData.Email))
            {
                user = await userManager.Value.FindByEmailAsync(authData.Email);
            }
            if (user is null && !string.IsNullOrEmpty(authData.PhoneNumber))
            {
                user = await userManager.Value.Users.FirstOrDefaultAsync(t => t.PhoneNumber == authData.PhoneNumber);
            }

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = identity,
                    Email = authData.Email,
                    PhoneNumber = authData.PhoneNumber,
                    RegistrationDate = DateTime.UtcNow,
                    Enabled = true,
                    ProfileVisibility = ProfileVisibility.Private,
                    FirstName = authData.FirstName,
                    LastName = authData.LastName,
                    Gender = authData.Gender,
                    Group = authData.Group,
                    CoreId = coreId,
                };

                var createResult = string.IsNullOrEmpty(password)
                    ? await userManager.Value.CreateAsync(user)
                    : await userManager.Value.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    return new(OperationResult.NotValid) { Errors = MapUserManagerErrors(createResult) };
                }

                await ApplyAvatarAsync(user, authData);
                _ = await userManager.Value.UpdateAsync(user);
                _ = await SyncRoleFromGroupAsync(user, authData.Group);
                await DefaultTeacherProfileToPublicAsync(user);
            }
            else
            {
                user.CoreId = coreId;
                // Deliberately outside the !ProfileUpdated guard below, unlike every other synced field here -
                // Group (teacher/student, see ApplicationUser.Group's doc comment) is the one profile value
                // gama-api keeps owning permanently: it's re-synced on every single legacy login, not just the
                // first one, so a Group change on gama-api's side takes effect here next time this user logs in.
                user.Group = authData.Group;
                if (!user.ProfileUpdated)
                {
                    await ApplyAvatarAsync(user, authData);
                    user.FirstName = authData.FirstName;
                    user.LastName = authData.LastName;
                    user.Gender = authData.Gender;
                    user.PhoneNumber ??= authData.PhoneNumber;
                }
                _ = await userManager.Value.UpdateAsync(user);
                _ = await SyncRoleFromGroupAsync(user, authData.Group);
            }

            return new(OperationResult.Succeeded)
            {
                Data = new LegacyBridgeTokenResponseDto
                {
                    UserId = user.Id,
                    Token = authData.Token,
                    ExpirationTime = validation.SecurityToken is JsonWebToken jwt ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) : null,
                },
            };
        }

        private async Task ApplyAvatarAsync(ApplicationUser user, LegacyAuthResponseDto authData)
        {
            if (authData.Avatar is null)
            {
                return;
            }

            using var stream = new MemoryStream(authData.Avatar.Content);
            var file = new FormFile(stream, 0, authData.Avatar.Content.LongLength, "Avatar", authData.Avatar.Name)
            {
                Headers = new HeaderDictionary(),
                ContentType = $"image/{Path.GetExtension(authData.Avatar.Name).Trim('.')}",
                ContentDisposition = new System.Net.Mime.ContentDisposition
                {
                    FileName = authData.Avatar.Name,
                }.ToString(),
            };
            var avatarResult = await fileService.Value.CreateFileAsync(new()
            {
                ContainerType = ContainerType.User,
                File = file,
            });
            user.AvatarId = avatarResult.Data;
        }

        /// <summary>
        /// Keeps Role.Teacher/Role.Student in sync with the gama-api-sourced Group signal (5 = Teacher, 6 =
        /// Student - see ApplicationUser.Group's doc comment) whenever a legacy login sets/updates it.
        /// Deliberately additive-plus-swap, never a full role replace: adds the matching role if missing,
        /// removes the *other* of Teacher/Student if present (so Role stays an accurate mirror of Group over
        /// time), but never touches any other role (Admin/Advisor/Finance) - a user who's also an Admin keeps
        /// that role regardless of what this does. Group values with no known Role mapping (null, 1, 2, 3, 7)
        /// leave existing Teacher/Student role membership untouched - guessing a removal for an unrecognized
        /// value would be worse than doing nothing.
        /// Best-effort: a failure here is logged and swallowed, never fails the surrounding login - a
        /// role-sync hiccup must not block someone from actually signing in.
        /// </summary>
        /// <returns>true if a role was actually added/removed; false if there was nothing to do (or group had no known mapping).</returns>
        private async Task<bool> SyncRoleFromGroupAsync(ApplicationUser user, int? group)
        {
            var targetRole = group switch
            {
                5 => nameof(Role.Teacher),
                6 => nameof(Role.Student),
                _ => null,
            };
            if (targetRole is null)
            {
                return false;
            }

            try
            {
                var otherRole = targetRole == nameof(Role.Teacher) ? nameof(Role.Student) : nameof(Role.Teacher);
                var currentRoles = await userManager.Value.GetRolesAsync(user);
                var changed = false;

                if (currentRoles.Contains(otherRole))
                {
                    _ = await userManager.Value.RemoveFromRoleAsync(user, otherRole);
                    changed = true;
                }

                if (!currentRoles.Contains(targetRole))
                {
                    _ = await userManager.Value.AddToRoleAsync(user, targetRole);
                    changed = true;
                }

                return changed;
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, "SyncRoleFromGroupAsync failed for user {UserId}, group {Group}", user.Id, group);
                return false;
            }
        }

        /// <summary>
        /// New-account-only default: a brand-new legacy-sync-created user who was just assigned Role.Teacher
        /// (via SyncRoleFromGroupAsync, called right before this) starts with a Public profile instead of the
        /// usual Private default, so teachers are discoverable via GET identities/profiles/list out of the box
        /// (that endpoint hard-filters to ProfileVisibility.Public - IdentitiesController.GetPublicProfile).
        /// Keyed off the actual Role.Teacher membership (not Group directly), so it only fires if the role sync
        /// above actually succeeded. Deliberately only called from the "create new user" branch of
        /// SyncLegacyAuthAsync - never re-applied on a later login/role change, so an existing user's own
        /// ProfileVisibility choice (via ManageProfileSettingsAsync) or an existing account's current setting is
        /// never overwritten. Best-effort, same as SyncRoleFromGroupAsync: a failure here must not block the
        /// surrounding login.
        /// </summary>
        private async Task DefaultTeacherProfileToPublicAsync(ApplicationUser user)
        {
            try
            {
                if (await userManager.Value.IsInRoleAsync(user, nameof(Role.Teacher)))
                {
                    user.ProfileVisibility = ProfileVisibility.Public;
                    _ = await userManager.Value.UpdateAsync(user);
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogError(exc, "DefaultTeacherProfileToPublicAsync failed for user {UserId}", user.Id);
            }
        }

        /// <summary>
        /// One-time-style backfill (added 2026-08-22): for every existing user with Group = 5 (Teacher) or 6
        /// (Student), applies the same role sync SyncLegacyAuthAsync now applies automatically on every legacy
        /// login (SyncRoleFromGroupAsync) - this is the explicit catch-up for the ~28,900 users who predate that
        /// auto-sync existing at all (see docs/business/identity-and-access.md's "Role.Teacher/Role.Student are
        /// now kept in sync" note - it explicitly does not backfill on its own). Additionally, for every Group = 5
        /// user, unconditionally sets ProfileVisibility = Public - deliberately, even for a user who already has
        /// some other value, since there's no way in the current data to tell "user's own deliberate choice" apart
        /// from "still on the untouched Private default" (unlike DefaultTeacherProfileToPublicAsync, which only
        /// ever touches brand-new accounts, this backfill is explicitly meant to touch existing ones - that's the
        /// whole point of it).
        /// Idempotent - safe to run more than once; a user who's already fully synced is simply a no-op (both
        /// SyncRoleFromGroupAsync and the ProfileVisibility check only act when something would actually change).
        /// Meant to run as a Hangfire background job, not inline in a request - see
        /// IdentitiesController(Admin).BackfillTeacherStudentRoles and ConvertAvatarsAsync's git history for why
        /// (this table is the same scale, tens of thousands of rows).
        /// </summary>
        public async Task<ResultData<BackfillTeacherStudentRolesResultDto>> BackfillRoleAndProfileVisibilityFromGroupAsync()
        {
            try
            {
                var candidates = await userManager.Value.Users.Where(t => t.Group == 5 || t.Group == 6).ToListAsync();
                var result = new BackfillTeacherStudentRolesResultDto { TotalCandidates = candidates.Count };

                foreach (var user in candidates)
                {
                    try
                    {
                        if (await SyncRoleFromGroupAsync(user, user.Group))
                        {
                            result.RoleChanged++;
                        }

                        if (user.Group == 5 && user.ProfileVisibility != ProfileVisibility.Public)
                        {
                            user.ProfileVisibility = ProfileVisibility.Public;
                            _ = await userManager.Value.UpdateAsync(user);
                            result.ProfileFlippedToPublic++;
                        }
                    }
                    catch (Exception exc)
                    {
                        result.Failed++;
                        Logger.Value.LogError(exc, "BackfillRoleAndProfileVisibilityFromGroupAsync failed for user {UserId}", user.Id);
                    }
                }

                if (Logger.Value.IsEnabled(LogLevel.Information))
                {
                    Logger.Value.LogInformation(
                        "BackfillRoleAndProfileVisibilityFromGroupAsync finished: {RoleChanged} roles changed, {ProfileFlipped} profiles flipped to Public, {Failed} failed (out of {Total} candidates)",
                        result.RoleChanged, result.ProfileFlippedToPublic, result.Failed, result.TotalCandidates);
                }

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Void>> AddLoginHistoryAsync([NotNull] LoginHistoryRequestDto requestDto)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                uow.GetRepository<LoginHistory>().Add(new()
                {
                    CreationDate = now,
                    UserId = requestDto.UserId,
                    IpAddress = requestDto.IpAddress,
                    UserAgent = requestDto.UserAgent,
                });
                _ = await uow.SaveChangesAsync();
                _ = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == requestDto.UserId).ExecuteUpdateAsync(t => t.SetProperty(p => p.LastLoginDate, now));

                return new(OperationResult.Succeeded);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<PublicProfileDto>>> GetProfilesListAsync(ListRequestDto<ApplicationUser>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = uow.GetRepository<ApplicationUser>().GetManyQueryable(requestDto?.Specification);
                int? total = requestDto?.PagingDto?.PageFilter?.ReturnTotalRecordsCount == true ? await lst.CountAsync() : null;

                var now = DateTimeOffset.UtcNow;
                var onlineCutoff = now - OnlineStatus.OnlineThreshold;
                var activeRecentlyCutoff = now - OnlineStatus.ActiveRecentlyThreshold;
                var onlineTodayCutoff = now - OnlineStatus.OnlineTodayThreshold;
                var activeThisWeekCutoff = now - OnlineStatus.ActiveThisWeekThreshold;
                var activeThisMonthCutoff = now - OnlineStatus.ActiveThisMonthThreshold;

                var query = lst.Select(t => new
                {
                    t.Id,
                    t.AvatarId,
                    t.FirstName,
                    t.LastName,
                    t.Skills,
                    t.Handle,
                    UserRateLevel = (string.IsNullOrEmpty(t.AvatarId) ? 0 : 15)
                        + (string.IsNullOrEmpty(t.FirstName) ? 0 : 5)
                        + (string.IsNullOrEmpty(t.LastName) ? 0 : 5)
                        + (!string.IsNullOrEmpty(t.CurrentStatusSentence) && t.CurrentStatusSentence.Length > 9 ? 10 : 0)
                        + (!string.IsNullOrEmpty(t.Biography) && t.Biography.Length > 49 ? 15 : 0)
                        + (!string.IsNullOrEmpty(t.Skills) && t.Skills.Length > 1 ? 20 : 0),
                    t.LastLoginDate,
                    // Lower = higher priority in the default sort; mirrors OnlineStatus.Calculate's
                    // tiers (0=Online .. 4=ActiveThisMonth), but a real "active long time ago" login
                    // (6) still ranks below a "never logged in" user (5) instead of tying with it.
                    // Written as a flat sum of independent conditions (not nested ternaries) so the
                    // whole thing stays one SQL-translatable expression the database can sort/page on.
                    ActivityRank = (t.LastLoginDate < onlineCutoff ? 1 : 0)
                        + (t.LastLoginDate < activeRecentlyCutoff ? 1 : 0)
                        + (t.LastLoginDate < onlineTodayCutoff ? 1 : 0)
                        + (t.LastLoginDate < activeThisWeekCutoff ? 1 : 0)
                        + (t.LastLoginDate < activeThisMonthCutoff ? 2 : 0)
                        + (t.LastLoginDate == null ? 5 : 0),
                });

                (query, var sortApplied) = query.OrderBy(requestDto?.PagingDto?.SortFilter);
                if (!sortApplied)
                {
                    query = query.OrderBy(t => t.ActivityRank).ThenByDescending(t => t.LastLoginDate).ThenByDescending(t => t.UserRateLevel);
                }
                if (requestDto?.PagingDto?.PageFilter is not null)
                {
                    query = query.Skip(requestDto.PagingDto.PageFilter.Skip)
                        .Take(requestDto.PagingDto.PageFilter.Size);
                }
                var items = await query.ToListAsync();
                if (items is null || items.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = new() { List = null } };
                }

                List<PublicProfileDto> result = new(items.Count);
                for (var i = 0; i < items.Count; i++)
                {
                    var skills = items[i].Skills?.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
                    var fullName = items[i].FirstName + " " + items[i].LastName;
                    result.Add(new()
                    {
                        Avatar = fileService.Value.GetStaticFileUrl(new() { FileId = items[i].AvatarId, ContainerType = ContainerType.User }),
                        FullName = fullName,
                        Handle = string.IsNullOrEmpty(items[i].Handle) ? $"{items[i].Id}-{fullName.Slugify()}" : items[i].Handle,
                        Skills = skills,
                        OnlineStatus = OnlineStatus.Calculate(items[i].LastLoginDate),
                        UserRateLevel = UserRateLevel.Calculate(items[i].UserRateLevel)
                    });
                }

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = total } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<PublicProfileResponseDto>> GetPublicProfileAsync([NotNull] PublicProfileRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();

                var userId = await repository.GetManyQueryable(t => t.Handle == requestDto.ProfileHandle).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                if (userId is null)
                {
                    var match = HandleRegex().Match(requestDto.ProfileHandle);
                    if (match.Success)
                    {
                        userId = match.Value.ValueOf<long?>();
                    }
                }

                if (userId is null)
                {
                    return new(OperationResult.NotFound);
                }

                var result = await repository.GetManyQueryable(t => t.Id == userId.Value).Select(t => new
                {
                    t.FirstName,
                    t.LastName,
                    t.RegistrationDate,
                    t.ProfileView,
                    t.Biography,
                    t.Skills,
                    t.AvatarId,
                    t.CurrentStatusSentence,
                    t.ProfileVisibility,
                    t.Id,
                    t.LastLoginDate,
                    t.OrphanDate,
                }).FirstOrDefaultAsync();
                if (result is null)
                {
                    return new(OperationResult.NotFound);
                }

                var valid = result.ProfileVisibility == ProfileVisibility.Public || result.Id == requestDto.UserId;
                if (!valid)
                {
                    if (result.ProfileVisibility == ProfileVisibility.Private)
                    {
                        return new(OperationResult.NotFound);
                    }

                    var connectionRepository = uow.GetRepository<Connection>();
                    var connected = await connectionRepository.AnyAsync(t => t.SourceUserId == requestDto.UserId && t.DestinationUserId == result.Id && t.Status == ConnectionStatus.Confirmed);
                    if (!connected)
                    {
                        return new(OperationResult.NotFound);
                    }

                }

                var experiences = await uow.GetRepository<Experience>().GetManyQueryable(t => t.UserId == result.Id).OrderByDescending(t => t.Id).Select(t => new ExperienceDto
                {
                    Id = t.Id,
                    SchoolId = t.SchoolId,
                    SchoolTitle = t.School.Name,
                    Description = t.Description,
                    StartDate = t.StartDate,
                    EndDate = t.EndDate,
                }).ToListAsync();

                _ = await repository.GetManyQueryable(t => t.Id == result.Id).ExecuteUpdateAsync(t => t.SetProperty(p => p.ProfileView, p => p.ProfileView + 1));

                var skills = result.Skills?.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        FirstName = result.FirstName,
                        LastName = result.LastName,
                        RegistrationDate = result.RegistrationDate,
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = result.AvatarId, ContainerType = ContainerType.User }),
                        ProfileView = result.ProfileView + 1,    //add current view
                        OnlineStatus = OnlineStatus.Calculate(result.LastLoginDate),
                        OrphanDate = result.Id == requestDto.UserId ? result.OrphanDate : null,
                        Biography = result.Biography,
                        Skills = skills,
                        CurrentStatusSentence = result.CurrentStatusSentence,
                        Experiences = experiences,
                        UserRateLevel = UserRateLevel.Calculate(result.AvatarId, result.FirstName, result.LastName, result.CurrentStatusSentence, result.Biography, skills, experiences?.Select(t => t.Id))
                    }
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ManageAvatarAsync([NotNull] ManageAvatarRequestDto requestDto)
        {
            try
            {
                var (avatarId, errors) = await SaveFileAsync(requestDto.Avatar);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == requestDto.UserId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.AvatarId, avatarId));

                return new(OperationResult.Succeeded)
                {
                    Data = affectedRows > 0,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> InitializeDeletingAccountAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();
                var now = DateTimeOffset.UtcNow;
                var affectedRows = await repository.GetManyQueryable(specification)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.OrphanDate, now));
                if (affectedRows > 0)
                {
                    var data = await repository.GetManyQueryable(specification).Select(t => new
                    {
                        t.FirstName,
                        t.LastName,
                        t.Email,
                    }).FirstOrDefaultAsync();
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.InitializeDeletingAccountEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", $"{data!.FirstName} {data.LastName}", StringComparison.OrdinalIgnoreCase)
                        .Replace("[DATE]", now.AddDays(7).ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "Deleting Account Request",
                        Body = template!,
                        EmailAddresses = [data!.Email!],
                    });
                }

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserCantBeRemoved"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<bool>> RecoverAccountAsync([NotNull] ISpecification<ApplicationUser> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();
                DateTimeOffset? orphanDate = null;
                var affectedRows = await repository.GetManyQueryable(specification).Where(t => t.OrphanDate != null)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.OrphanDate, orphanDate));

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = new[] { new Error { Message = Localizer.Value["UserCantBeRemoved"], } } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<string>> ValidateHandleAsync([NotNull] ValidateHandleRequestDto requestDto)
        {
            try
            {
                if (string.IsNullOrEmpty(requestDto.Handle))
                {
                    return new(OperationResult.Succeeded);
                }

                var value = Globals.Slugify(requestDto.Handle);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<ApplicationUser>().AnyAsync(t => t.Handle == value && t.Id != requestDto.UserId);

                return exists
                    ? new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidHandle"] }] }
                    : new(OperationResult.Succeeded) { Data = value };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #region SiteMap

        public ItemType ItemType => ItemType.Profile;

        public IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow) => uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Enabled && t.ProfileVisibility == ProfileVisibility.Public).Select(t => new SiteMapItemDto
        {
            Id = t.Id,
            Path1 = t.Handle,
            Path2 = string.IsNullOrEmpty(t.Handle) ? t.Id + "-" + t.FirstName + "-" + t.LastName : null,
            LastModifyDate = t.RegistrationDate,
        });

        #endregion

        #region Job

        public async Task<ResultData<bool>> UpdateOrphanUsersAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ApplicationUser>();
                var date = DateTimeOffset.UtcNow.AddDays(-7);
                var lst = await repository.GetManyQueryable(t => t.OrphanDate != null && date >= t.OrphanDate.Value).Select(t => new
                {
                    t.Id,
                    t.Email,
                    t.FirstName,
                    t.LastName,
                    t.AvatarId,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded);
                }

                string? nullString = null;
                DateTimeOffset? nullDate = null;
                for (var i = 0; i < lst.Count; i++)
                {
                    try
                    {
                        var data = lst[i];
                        var startTemplate = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.StartDeletingAccountEmailTemplate))).Data;
                        startTemplate = startTemplate?
                            .Replace("[RECEIVER_NAME]", $"{data.FirstName} {data.LastName}", StringComparison.OrdinalIgnoreCase)
                            .Replace("[DATE]", DateTimeOffset.UtcNow.ToString(), StringComparison.OrdinalIgnoreCase);
                        _ = await emailService.Value.SendEmailAsync(new()
                        {
                            Subject = "Initialize Deleting Account",
                            Body = startTemplate!,
                            EmailAddresses = [data.Email!],
                        });

                        _ = await fileService.Value.RemoveFileAsync(new() { FileId = data.AvatarId, ContainerType = ContainerType.User });

                        //clear user data
                        _ = await repository.GetManyQueryable(t => t.Id == data.Id).ExecuteUpdateAsync(t => t
                            .SetProperty(t => t.FirstName, "Deleted")
                            .SetProperty(t => t.LastName, "Account")
                            .SetProperty(t => t.Email, nullString)
                            .SetProperty(t => t.NormalizedEmail, nullString)
                            .SetProperty(t => t.ProfileView, 0)
                            .SetProperty(t => t.AvatarId, nullString)
                            .SetProperty(t => t.Biography, nullString)
                            .SetProperty(t => t.WalletId, nullString)
                            .SetProperty(t => t.Skills, nullString)
                            .SetProperty(t => t.CurrentStatusSentence, nullString)
                            .SetProperty(t => t.OrphanDate, nullDate));

                        //delete Experience entries
                        _ = await uow.GetRepository<Experience>().GetManyQueryable(t => t.UserId == data.Id).ExecuteDeleteAsync();

                        //delete Follow relationships
                        ConnectionStatus[] statuses = [ConnectionStatus.Confirmed, ConnectionStatus.Requested];
                        _ = await uow.GetRepository<Connection>().GetManyQueryable(t => t.SourceUserId == data.Id && statuses.Contains(t.Status)).ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, ConnectionStatus.Revoked));


                        var finishedtemplate = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.FinishedDeletingAccountEmailTemplate))).Data;
                        finishedtemplate = finishedtemplate?
                            .Replace("[RECEIVER_NAME]", $"{data.FirstName} {data.LastName}", StringComparison.OrdinalIgnoreCase)
                            .Replace("[DATE]", DateTimeOffset.UtcNow.ToString(), StringComparison.OrdinalIgnoreCase);
                        _ = await emailService.Value.SendEmailAsync(new()
                        {
                            Subject = "Finish Deleting Account",
                            Body = finishedtemplate!,
                            EmailAddresses = [data.Email!],
                        });
                    }
                    catch (Exception exc)
                    {
                        Logger.Value.LogException(exc);
                    }
                }

                return new(OperationResult.Succeeded);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<long>> ResolveUserIdAsync(long id, [NotNull] IdentifierType idType)
        {
            try
            {
                if (idType == IdentifierType.Id)
                {
                    return new(OperationResult.Succeeded) { Data = id };
                }

                var userId = await userManager.Value.Users.Where(t => t.CoreId == id).Select(t => t.Id).FirstOrDefaultAsync();
                return userId == 0
                    ? new(OperationResult.NotFound) { Errors = [new Error { Message = Localizer.Value["UserNotFound"] }] }
                    : new(OperationResult.Succeeded) { Data = userId };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<Dictionary<long, long>>> ResolveUserIdsAsync([NotNull] IEnumerable<long> ids, [NotNull] IdentifierType idType)
        {
            try
            {
                var idList = ids.Distinct().ToList();
                if (idType == IdentifierType.Id)
                {
                    return new(OperationResult.Succeeded) { Data = idList.ToDictionary(t => t, t => t) };
                }

                var map = await userManager.Value.Users.Where(t => t.CoreId != null && idList.Contains(t.CoreId!.Value))
                    .Select(t => new { CoreId = t.CoreId!.Value, t.Id })
                    .ToDictionaryAsync(t => t.CoreId, t => t.Id);
                return new(OperationResult.Succeeded) { Data = map };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        #endregion

        private async Task<(string? AvatarId, IEnumerable<Error>? Errors)> SaveFileAsync(IFormFile? file)
        {
            if (file is null)
            {
                return (null, null);
            }

            var fileId = await fileService.Value.CreateFileAsync(new()
            {
                File = file,
                ContainerType = ContainerType.User,
            });

            return (fileId.Data, fileId.Errors);
        }

        private async Task<string> GetTimeZoneIdAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var timeZoneId = await uow.GetRepository<ApplicationUserClaim, int>().GetManyQueryable(t => t.UserId == userId && t.ClaimType == TimeZoneIdClaim)
                    .Select(t => t.ClaimValue).FirstOrDefaultAsync();

                return !string.IsNullOrEmpty(timeZoneId) ? timeZoneId : UtcTimeZoneId;
            }
            catch
            {
                return UtcTimeZoneId;
            }
        }

        private static IEnumerable<Error> MapUserManagerErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                yield return new Error { Message = error.Description };
            }
        }

        private static string? GenerateDeviceHash(HttpContext? httpContext)
        {
            var ip = httpContext.GetClientIpAddress();
            var userAgent = httpContext.UserAgent();

            var byteValue = Encoding.UTF8.GetBytes(ip + userAgent);
            var byteHash = SHA256.HashData(byteValue);
            return Convert.ToBase64String(byteHash);
        }

        private ResultData<T> ValidateUser<T>(ApplicationUser? user)
        {
            IEnumerable<Error> errors = [];
            if (user is null)
            {
                errors = [new() { Message = Localizer.Value["UserNotFound"] }];
            }
            else if (!user.Enabled)
            {
                errors = [new() { Message = Localizer.Value["UserNotEnabled"] }];
            }

            return new(user?.Enabled == true ? OperationResult.Succeeded : OperationResult.NotValid)
            {
                Errors = errors,
            };
        }

        [GeneratedRegex("^\\d+")]
        private static partial Regex HandleRegex();
    }
}

