namespace GamaEdtech.Infrastructure.Provider.Authentication
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Mapping;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Google.Apis.Auth;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class GoogleAuthenticationProvider(Lazy<ILogger<GoogleAuthenticationProvider>> logger
        , Lazy<SignInManager<ApplicationUser>> signInManager, Lazy<IStringLocalizer<GoogleAuthenticationProvider>> localizer
        , Lazy<IConfiguration> configuration) : IAuthenticationProvider
    {
        public AuthenticationProvider ProviderType => AuthenticationProvider.Google;

        public async Task<ResultData<AuthenticationResponseDto>> AuthenticateAsync([NotNull] AuthenticationRequestDto requestDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestDto.Username))
                {
                    return InvalidToken();
                }

                var clientId = configuration.Value.GetValue<string>("Authentication:Google:ClientId");
                if (string.IsNullOrEmpty(clientId))
                {
                    logger.Value.LogError("Google sign-in rejected: Authentication:Google:ClientId is not configured");
                    return InvalidToken();
                }

                // Verifies the ID token the way Google requires: signature against Google's published keys (fetched and
                // cached by the library), issuer, expiry, and that it was issued for OUR client (audience). The previous
                // code checked only the issuer and skipped the signature, so a hand-written token claiming any email was
                // accepted - a full account takeover. See docs/api/authentication.md.
                GoogleJsonWebSignature.Payload payload;
                try
                {
                    payload = await GoogleJsonWebSignature.ValidateAsync(requestDto.Username, new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = [clientId],
                    });
                }
                catch (InvalidJwtException exc)
                {
                    logger.Value.LogWarning(exc, "Google sign-in rejected: invalid ID token");
                    return InvalidToken();
                }

                // The account is looked up (or created) by this email, so it must be one Google has verified.
                if (string.IsNullOrEmpty(payload.Email) || !payload.EmailVerified)
                {
                    logger.Value.LogWarning("Google sign-in rejected: token has no verified email");
                    return InvalidToken();
                }

                var email = payload.Email;
                var userManager = signInManager.Value.UserManager;
                var user = await userManager.FindByNameAsync(email);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        // Google verified the address (checked above).
                        EmailConfirmed = true,
                        RegistrationDate = DateTime.UtcNow,
                        Enabled = true,
                        FirstName = payload.GivenName,
                        LastName = payload.FamilyName,
                    };
                    var identityResult = await userManager.CreateAsync(user);
                    if (!identityResult.Succeeded)
                    {
                        return new(OperationResult.NotValid) { Errors = identityResult.Errors.Select(t => new Error { Message = t.Description, Code = t.Code }) };
                    }
                }
                else if (!user.EmailConfirmed && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    // Signing in with Google proves ownership of the account's own address.
                    user.EmailConfirmed = true;
                    _ = await userManager.UpdateAsync(user);
                }

                var validationResult = ValidateUser<AuthenticationResponseDto>(user);
                if (validationResult.OperationResult is not OperationResult.Succeeded)
                {
                    return validationResult;
                }

                var dto = user.AdaptData<ApplicationUser, ApplicationUserDto>();
                return new(OperationResult.Succeeded)
                {
                    Data = new() { User = dto, }
                };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        private ResultData<AuthenticationResponseDto> InvalidToken() => new(OperationResult.NotValid)
        {
            Errors = [new Error { Message = localizer.Value["WrongUsernameOrPassword"] }],
        };

        private ResultData<T> ValidateUser<T>(ApplicationUser user) => user.Enabled
                ? new(OperationResult.Succeeded)
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = localizer.Value["UserNotEnabled"] }],
                };
    }
}
