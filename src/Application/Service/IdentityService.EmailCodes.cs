namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.AspNetCore.Identity;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    /// <summary>
    /// Registering and resetting a forgotten password with an emailed code, in gama-api's three steps (ask for a code, check it,
    /// then set the password), and the one-time gama-api password check that moves a person's password here on sign-in. See
    /// docs/business/identity-and-access.md, "Sign-up and password reset with an emailed code".
    /// </summary>
    public partial class IdentityService
    {
        public async Task<ResultData<string>> SendRegistrationCodeAsync([NotNull] string email)
        {
            try
            {
                var valid = IsEmail(email);
                var taken = valid && await FindByEmailOrUserNameAsync(email) is not null;
                var refusal = (valid, taken) switch
                {
                    (false, _) => "Enter a valid email address.",
                    (_, true) => "This email already has an account. Sign in, or reset your password.",
                    _ => null,
                };
                return refusal is not null
                    ? new(OperationResult.NotValid) { Errors = [new() { Message = refusal }] }
                    : await SendCodeAsync(EmailCode.Purpose.Register, email, name: null, nameof(ApplicationSettingsDto.RegistrationCodeEmailTemplate),
                        new ApplicationSettingsDto().RegistrationCodeEmailTemplate!, "Gamatrain - your sign-up code");
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public Task<ResultData<Void>> VerifyRegistrationCodeAsync([NotNull] string email, string? code) =>
            VerifyCodeAsync(EmailCode.Purpose.Register, email, code);

        public async Task<ResultData<string>> SendPasswordResetCodeAsync([NotNull] string email)
        {
            try
            {
                var user = IsEmail(email) ? await FindByEmailOrUserNameAsync(email) : null;
                var refusal = user switch
                {
                    _ when !IsEmail(email) => "Enter a valid email address.",
                    null => "No account uses this email. Check it, or sign up.",
                    { Enabled: false } => Localizer.Value["UserNotEnabled"],
                    _ => null,
                };
                return refusal is not null || user is null
                    ? new(OperationResult.NotValid) { Errors = [new() { Message = refusal }] }
                    : await SendCodeAsync(EmailCode.Purpose.ResetPassword, email, $"{user.FirstName} {user.LastName}".Trim(),
                        nameof(ApplicationSettingsDto.PasswordResetCodeEmailTemplate), new ApplicationSettingsDto().PasswordResetCodeEmailTemplate!,
                        "Gamatrain - your password reset code");
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public Task<ResultData<Void>> VerifyPasswordResetCodeAsync([NotNull] string email, string? code) =>
            VerifyCodeAsync(EmailCode.Purpose.ResetPassword, email, code);

        public async Task<ResultData<long>> ResetPasswordWithCodeAsync([NotNull] string email, string? code, [NotNull] string password)
        {
            try
            {
                var user = await FindByEmailOrUserNameAsync(email);
                var check = await EmailCode.CheckAsync(cacheProvider.Value, EmailCode.Purpose.ResetPassword, email, code, useUp: false);
                if (user is null || check is not EmailCode.CheckResult.Valid)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = EmailCode.Message(user is null ? EmailCode.CheckResult.Missing : check) }] };
                }

                // ResetPasswordAsync also rotates the security stamp: every other session of this user ends, as it should after a reset.
                var token = await userManager.Value.GeneratePasswordResetTokenAsync(user);
                var result = await userManager.Value.ResetPasswordAsync(user, token, password);
                if (!result.Succeeded)
                {
                    return new(OperationResult.NotValid) { Errors = MapUserManagerErrors(result) };
                }

                _ = await EmailCode.CheckAsync(cacheProvider.Value, EmailCode.Purpose.ResetPassword, email, code, useUp: true);

                // The code proved the address, and a lockout from wrong passwords shouldn't outlive the new one.
                user = await userManager.Value.FindByIdAsync(user.Id.ToString(CultureInfo.InvariantCulture));
                if (user is not null)
                {
                    user.EmailConfirmed = true;
                    _ = await userManager.Value.UpdateAsync(user);
                    _ = await userManager.Value.ResetAccessFailedCountAsync(user);
                    _ = await userManager.Value.SetLockoutEndDateAsync(user, null);
                }

                return new(OperationResult.Succeeded) { Data = user?.Id ?? 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<string>> SendLoginCodeAsync([NotNull] string email)
        {
            try
            {
                var user = IsEmail(email) ? await FindByEmailOrUserNameAsync(email) : null;
                var refusal = user switch
                {
                    _ when !IsEmail(email) => "Enter a valid email address.",
                    null => "No account uses this email. Check it, or sign up.",
                    { Enabled: false } => "This account is disabled. Contact support.",
                    _ => null,
                };
                return refusal is not null || user is null
                    ? new(OperationResult.NotValid) { Errors = [new() { Message = refusal }] }
                    : await SendCodeAsync(EmailCode.Purpose.Login, email, $"{user.FirstName} {user.LastName}".Trim(),
                        nameof(ApplicationSettingsDto.LoginCodeEmailTemplate), new ApplicationSettingsDto().LoginCodeEmailTemplate!,
                        "Gamatrain - your sign-in code");
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> SignInWithCodeAsync([NotNull] string email, string? code)
        {
            try
            {
                var check = await EmailCode.CheckAsync(cacheProvider.Value, EmailCode.Purpose.Login, email, code, useUp: true);
                var user = check is EmailCode.CheckResult.Valid ? await FindByEmailOrUserNameAsync(email) : null;
                if (user is null || !user.Enabled)
                {
                    var message = check is EmailCode.CheckResult.Valid ? "This account is disabled. Contact support." : EmailCode.Message(check);
                    return new(OperationResult.NotValid) { Errors = [new() { Message = message }] };
                }

                // The code proved the address. A lockout from wrong passwords doesn't block it (as gama-api switched a locked account to
                // codes), and is cleared: the person has shown they own the account.
                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    _ = await userManager.Value.UpdateAsync(user);
                }

                _ = await userManager.Value.ResetAccessFailedCountAsync(user);
                _ = await userManager.Value.SetLockoutEndDateAsync(user, null);
                return new(OperationResult.Succeeded) { Data = user.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> SignInWithLegacyPasswordAsync([NotNull] string identity, [NotNull] string password)
        {
            try
            {
                // A local account that is locked out (too many wrong passwords) or disabled is never retried on gama-api: that would get
                // around the lockout.
                var local = await FindByEmailOrUserNameAsync(identity);
                if (local is not null && (!local.Enabled || await userManager.Value.IsLockedOutAsync(local)))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value[local.Enabled ? "UserLockedOut" : "UserNotEnabled"] }] };
                }

                var legacy = await LegacyLoginAsync(new() { Identity = identity, Password = password });
                if (legacy is not { OperationResult: OperationResult.Succeeded, Data: { Token: not null, UserId: > 0 } data })
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["WrongUsernameOrPassword"] }] };
                }

                // gama-api accepted the password: keep it here. Set straight on the hash (not Remove/AddPassword), so the security stamp
                // stays and the person's other sessions keep working.
                var user = await userManager.Value.FindByIdAsync(data.UserId.ToString(CultureInfo.InvariantCulture));
                if (user is null || !user.Enabled)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value[user is null ? "UserNotFound" : "UserNotEnabled"] }] };
                }

                if (user.PasswordHash is null || userManager.Value.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
                {
                    user.PasswordHash = userManager.Value.PasswordHasher.HashPassword(user, password);
                    _ = await userManager.Value.UpdateAsync(user);
                }

                return new(OperationResult.Succeeded) { Data = user.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private async Task<ResultData<Void>> VerifyCodeAsync(EmailCode.Purpose purpose, string email, string? code)
        {
            try
            {
                var check = await EmailCode.CheckAsync(cacheProvider.Value, purpose, email, code, useUp: false);
                return check is EmailCode.CheckResult.Valid
                    ? new(OperationResult.Succeeded)
                    : new(OperationResult.NotValid) { Errors = [new() { Message = EmailCode.Message(check) }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private async Task<ResultData<string>> SendCodeAsync(EmailCode.Purpose purpose, string email, string? name, string templateKey, string defaultTemplate, string subject)
        {
            var code = await EmailCode.IssueAsync(cacheProvider.Value, purpose, email);
            if (code is null)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "A code was just sent. Wait a minute before asking for another." }] };
            }

            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(templateKey)).Data;
            var body = (string.IsNullOrWhiteSpace(template) ? defaultTemplate : template)
                .Replace("[RECEIVER_NAME]", WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name), StringComparison.OrdinalIgnoreCase)
                .Replace("[CODE]", code, StringComparison.OrdinalIgnoreCase)
                .Replace("[MINUTES]", EmailCode.Lifetime.TotalMinutes.ToString("0", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

            var address = email.Trim();
            var sent = await emailService.Value.SendEmailAsync(new()
            {
                Subject = subject,
                Body = body,
                EmailAddresses = [address],
                From = emailService.Value.GetNoReplyEmail(),
            });
            return sent.OperationResult is not OperationResult.Succeeded
                ? new(sent.OperationResult) { Errors = sent.Errors }
                : new(OperationResult.Succeeded) { Data = TwoFactorService.MaskEmail(address) };
        }

        /// <summary>An account by its email (its identity), else by its user name (the same email, in case an account's differs).</summary>
        private async Task<ApplicationUser?> FindByEmailOrUserNameAsync(string email)
        {
            var normalized = email.Trim();
            return await userManager.Value.FindByEmailAsync(normalized) ?? await userManager.Value.FindByNameAsync(normalized);
        }

        private static bool IsEmail(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value.Trim());
    }
}
