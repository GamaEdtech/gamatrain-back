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

    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class LocalAuthenticationProvider(Lazy<ILogger<LocalAuthenticationProvider>> logger
        , Lazy<SignInManager<ApplicationUser>> signInManager) : IAuthenticationProvider
    {
        public AuthenticationProvider ProviderType => AuthenticationProvider.Local;

        public async Task<ResultData<AuthenticationResponseDto>> AuthenticateAsync([NotNull] AuthenticationRequestDto requestDto)
        {
            try
            {
                // People sign in with their email, which is the account's identity; the user name is the same email, and is only
                // tried in case an account's differs.
                var userManager = signInManager.Value.UserManager;
                var identity = requestDto.Username.Trim();
                var user = await userManager.FindByEmailAsync(identity) ?? await userManager.FindByNameAsync(identity);
                var validationResult = ValidateUser<AuthenticationResponseDto>(user);

                if (validationResult.OperationResult is not OperationResult.Succeeded)
                {
                    return validationResult;
                }

                var signinResult = await signInManager.Value.CheckPasswordSignInAsync(user!, requestDto.Password!, true);
                var failure = signinResult.IsLockedOut ? LockedOut() : WrongPassword();
                return signinResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = new AuthenticationResponseDto { User = user!.AdaptData<ApplicationUser, ApplicationUserDto>() } }
                    : new(OperationResult.NotValid) { Errors = [failure] };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        private static ResultData<T> ValidateUser<T>(ApplicationUser? user)
        {
            IEnumerable<Error> errors = [];
            if (user is null)
            {
                // The same answer as a wrong password: the sign-in form doesn't tell which emails have an account.
                errors = [WrongPassword()];
            }
            else if (!user.Enabled)
            {
                errors = [new() { Message = "This account is disabled. Contact support.", Reference = "disabled" }];
            }

            return new(user?.Enabled == true ? OperationResult.Succeeded : OperationResult.NotValid)
            {
                Errors = errors,
            };
        }

        // Plain messages (the localizer had no text for these keys, so the website showed "UserNotFound"), with a Reference the
        // website acts on: after "wrongPassword" it offers a sign-in code by email, as gama-api did.
        private static Error WrongPassword() => new() { Message = "Wrong email or password.", Reference = "wrongPassword" };

        private static Error LockedOut() => new() { Message = "Too many wrong passwords. Try again later, or sign in with a code sent to your email.", Reference = "lockedOut" };
    }
}
