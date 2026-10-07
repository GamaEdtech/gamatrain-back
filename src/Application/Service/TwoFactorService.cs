namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.TwoFactor;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Built on ASP.NET Identity's own authenticator support (AuthenticatorTokenProvider, registered by
    /// AddDefaultTokenProviders; the key lives in ApplicationUserToken). "Enabled" means TwoFactorEnabled AND a key is
    /// stored. Two deliberate departures from Identity's helpers:
    /// - The key is written through the store, not UserManager.ResetAuthenticatorKeyAsync, and the flag through
    ///   UpdateAsync, not SetTwoFactorEnabledAsync: both helpers rotate the security stamp, which invalidates every
    ///   bearer token this API issued to the user (ApiDataProtectorTokenProvider validates the stamp) - setting up 2FA
    ///   would sign the admin out mid-setup.
    /// - Identity accepts a code anywhere in its time window as often as it's sent. Here a code is single-use (Redis
    ///   marker) and wrong codes are capped per user (MaxFailedAttempts per FailedAttemptsWindow), since 6 digits alone
    ///   are brute-forceable.
    /// Setting up an authenticator (first time, or again after disable/reset) also needs a code emailed to the user's
    /// confirmed address (SendSetupEmailCodeAsync, 2026-10-07): a stolen password alone can't enrol the attacker's app.
    /// </summary>
    public class TwoFactorService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<TwoFactorService>> localizer, Lazy<ILogger<TwoFactorService>> logger
        , Lazy<UserManager<ApplicationUser>> userManager, Lazy<IUserStore<ApplicationUser>> userStore, Lazy<ICacheProvider> cacheProvider
        , Lazy<IEmailService> emailService)
        : LocalizableServiceBase<TwoFactorService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ITwoFactorService
    {
        /// <summary>Shown as the account's label in the authenticator app.</summary>
        private const string Issuer = "Gamatrain";
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan FailedAttemptsWindow = TimeSpan.FromMinutes(15);

        /// <summary>Longer than AuthenticatorTokenProvider's acceptance window (current step +/- 2 x 30s), so a used code can't be replayed while still valid.</summary>
        private static readonly TimeSpan UsedCodeLifetime = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan SetupEmailCodeLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SetupEmailResendInterval = TimeSpan.FromSeconds(60);

        public async Task<ResultData<TwoFactorStatusDto>> GetStatusAsync(long userId)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
                return user is null
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] }] }
                    : new(OperationResult.Succeeded) { Data = new() { Enabled = await IsEnabledAsync(user) } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<string>> SendSetupEmailCodeAsync(long userId)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] }] };
                }

                if (await IsEnabledAsync(user))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = AlreadyEnabledMessage }] };
                }

                if (!user.EmailConfirmed || string.IsNullOrEmpty(user.Email))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Your account has no confirmed email address, so two-factor setup can't be verified. Confirm your email first." }] };
                }

                var userKey = user.Id.ToString(CultureInfo.InvariantCulture);
                var resendKey = $"TwoFactorSetupEmailSent_{userKey}";
                if (await cacheProvider.Value.GetAsync<bool>(resendKey))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "A code was just sent. Wait a minute before asking for another." }] };
                }

                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
                await cacheProvider.Value.SetAsync($"TwoFactorSetupEmailCode_{userKey}", new SetupEmailCode { Hash = HashCode(code), Attempts = 0 }
                    , new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SetupEmailCodeLifetime });
                await cacheProvider.Value.SetAsync(resendKey, true, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SetupEmailResendInterval });

                var sent = await emailService.Value.SendEmailAsync(new()
                {
                    Subject = "Gamatrain - two-factor setup code",
                    Body = $"Your code to set up two-factor authentication is <b>{code}</b>. It expires in {SetupEmailCodeLifetime.TotalMinutes:0} minutes.<br><br>"
                        + "If you didn't start this, someone may have your password: change it now and tell the other admins.",
                    EmailAddresses = [user.Email],
                    From = emailService.Value.GetNoReplyEmail(),
                });
                if (sent.OperationResult is not OperationResult.Succeeded)
                {
                    return new(sent.OperationResult) { Errors = sent.Errors };
                }

                if (Logger.Value.IsEnabled(LogLevel.Information))
                {
                    Logger.Value.LogInformation("Two-factor setup email code sent to UserId {UserId}", userId);
                }

                return new(OperationResult.Succeeded) { Data = MaskEmail(user.Email) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<AuthenticatorSetupDto>> BeginSetupAsync(long userId, string? emailCode)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] }] };
                }

                if (await IsEnabledAsync(user))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = AlreadyEnabledMessage }] };
                }

                var emailCheck = await CheckSetupEmailCodeAsync(user.Id, emailCode);
                if (emailCheck.OperationResult is not OperationResult.Succeeded)
                {
                    return new(emailCheck.OperationResult) { Errors = emailCheck.Errors };
                }

                var key = userManager.Value.GenerateNewAuthenticatorKey();
                await ((IUserAuthenticatorKeyStore<ApplicationUser>)userStore.Value).SetAuthenticatorKeyAsync(user, key, CancellationToken.None);
                var updateResult = await userManager.Value.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = updateResult.Errors.Select(t => new Error { Message = t.Description }) };
                }

                var account = user.Email ?? user.UserName ?? user.Id.ToString(CultureInfo.InvariantCulture);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        SharedKey = key,
                        AuthenticatorUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6",
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> EnableAsync(long userId, string? code)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] }] };
                }

                if (await IsEnabledAsync(user))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Two-factor authentication is already enabled." }] };
                }

                if (string.IsNullOrEmpty(await userManager.Value.GetAuthenticatorKeyAsync(user)))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Start the authenticator setup first." }] };
                }

                var verification = await CheckCodeAsync(user, code);
                if (verification.OperationResult is not OperationResult.Succeeded)
                {
                    return verification;
                }

                user.TwoFactorEnabled = true;
                var updateResult = await userManager.Value.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = updateResult.Errors.Select(t => new Error { Message = t.Description }) };
                }

                if (Logger.Value.IsEnabled(LogLevel.Information))
                {
                    Logger.Value.LogInformation("Two-factor authentication enabled for UserId {UserId}", userId);
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> DisableAsync(long userId, string? code)
        {
            try
            {
                var verification = await VerifyCodeAsync(userId, code);
                if (verification.OperationResult is not OperationResult.Succeeded)
                {
                    return verification;
                }

                var result = await TurnOffAsync(userId);
                if (result.OperationResult is OperationResult.Succeeded && Logger.Value.IsEnabled(LogLevel.Information))
                {
                    Logger.Value.LogInformation("Two-factor authentication disabled by UserId {UserId}", userId);
                }

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> ResetForUserAsync(long adminUserId, string? adminCode, long targetUserId)
        {
            try
            {
                if (adminUserId == targetUserId)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Use disable for your own account; a reset must be done by another admin." }] };
                }

                var verification = await VerifyCodeAsync(adminUserId, adminCode);
                if (verification.OperationResult is not OperationResult.Succeeded)
                {
                    return verification;
                }

                var result = await TurnOffAsync(targetUserId);
                if (result.OperationResult is OperationResult.Succeeded)
                {
                    Logger.Value.LogWarning("Two-factor authentication reset for UserId {TargetUserId} by admin UserId {AdminUserId}", targetUserId, adminUserId);
                }

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> VerifyCodeAsync(long userId, string? code)
        {
            try
            {
                var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
                return user is not null && await IsEnabledAsync(user)
                    ? await CheckCodeAsync(user, code)
                    : new(OperationResult.NotValid) { Errors = [new() { Message = "Set up two-factor authentication (an authenticator app) before performing this action." }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The emailed setup code: single use, MaxFailedAttempts wrong tries, then a new one must be requested.</summary>
        private async Task<ResultData<bool>> CheckSetupEmailCodeAsync(long userId, string? emailCode)
        {
            var cacheKey = $"TwoFactorSetupEmailCode_{userId.ToString(CultureInfo.InvariantCulture)}";
            var stored = await cacheProvider.Value.GetAsync<SetupEmailCode>(cacheKey);
            if (stored?.Hash is null)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "Request an email code first (it expires after 10 minutes)." }] };
            }

            var normalized = emailCode?.Trim() ?? string.Empty;
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored.Hash), Convert.FromHexString(HashCode(normalized))))
            {
                stored.Attempts++;
                if (stored.Attempts >= MaxFailedAttempts)
                {
                    await cacheProvider.Value.RemoveAsync(cacheKey);
                    Logger.Value.LogWarning("Two-factor setup email code for UserId {UserId} invalidated after {Attempts} wrong tries", userId, stored.Attempts);
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Too many wrong codes. Request a new email code." }] };
                }

                await cacheProvider.Value.SetAsync(cacheKey, stored, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SetupEmailCodeLifetime });
                return new(OperationResult.NotValid) { Errors = [new() { Message = "The email code is not valid." }] };
            }

            await cacheProvider.Value.RemoveAsync(cacheKey);
            return new(OperationResult.Succeeded) { Data = true };
        }

        private static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

        /// <summary>"sadeq@example.com" -> "s***q@example.com", enough for the user to recognise their inbox.</summary>
        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0)
            {
                return "***";
            }

            var name = email[..at];
            var masked = name.Length <= 2 ? $"{name[0]}***" : $"{name[0]}***{name[^1]}";
            return masked + email[at..];
        }

        private const string AlreadyEnabledMessage = "Two-factor authentication is already enabled. Disable it with a current code before setting up a new authenticator.";

        private async Task<bool> IsEnabledAsync(ApplicationUser user) =>
            user.TwoFactorEnabled && !string.IsNullOrEmpty(await userManager.Value.GetAuthenticatorKeyAsync(user));

        private async Task<ResultData<bool>> TurnOffAsync(long userId)
        {
            var user = await userManager.Value.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
            if (user is null)
            {
                return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] }] };
            }

            // The old key stays stored but is dead: "enabled" also requires the flag, and the next setup overwrites it.
            user.TwoFactorEnabled = false;
            var updateResult = await userManager.Value.UpdateAsync(user);
            return updateResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = true }
                : new(OperationResult.Failed) { Errors = updateResult.Errors.Select(t => new Error { Message = t.Description }) };
        }

        private async Task<ResultData<bool>> CheckCodeAsync(ApplicationUser user, string? code)
        {
            var userKey = user.Id.ToString(CultureInfo.InvariantCulture);
            var failedKey = $"TwoFactorFailed_{userKey}";
            var failedAttempts = await cacheProvider.Value.GetAsync<int>(failedKey);
            if (failedAttempts >= MaxFailedAttempts)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "Too many wrong two-factor codes. Try again in 15 minutes." }] };
            }

            var normalized = code?.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(normalized) || normalized.Length != 6 || !normalized.All(char.IsAsciiDigit))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter the 6-digit code from your authenticator app." }] };
            }

            var usedKey = $"TwoFactorUsed_{userKey}_{normalized}";
            if (await cacheProvider.Value.GetAsync<bool>(usedKey))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "This code was already used. Wait for the next code." }] };
            }

            var valid = await userManager.Value.VerifyTwoFactorTokenAsync(user, userManager.Value.Options.Tokens.AuthenticatorTokenProvider, normalized);
            if (!valid)
            {
                await cacheProvider.Value.SetAsync(failedKey, failedAttempts + 1, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = FailedAttemptsWindow });
                Logger.Value.LogWarning("Wrong two-factor code for UserId {UserId} (attempt {Attempt})", user.Id, failedAttempts + 1);
                return new(OperationResult.NotValid) { Errors = [new() { Message = "The two-factor code is not valid." }] };
            }

            await cacheProvider.Value.SetAsync(usedKey, true, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = UsedCodeLifetime });
            await cacheProvider.Value.RemoveAsync(failedKey);
            return new(OperationResult.Succeeded) { Data = true };
        }

        private sealed class SetupEmailCode
        {
            public string? Hash { get; set; }

            public int Attempts { get; set; }
        }
    }
}
