namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.TwoFactor;

    /// <summary>
    /// Authenticator-app (TOTP, e.g. Google Authenticator) second factor for sensitive admin actions (2026-10-07).
    /// This is a step-up check on individual actions, not a login factor: sign-in never asks for a code. A sensitive
    /// action calls <see cref="VerifyCodeAsync"/> with the code the caller sent alongside the request.
    /// See docs/business/identity-and-access.md, "Authenticator two-factor for admin actions".
    /// </summary>
    [Injectable]
    public interface ITwoFactorService
    {
        Task<ResultData<TwoFactorStatusDto>> GetStatusAsync(long userId);

        /// <summary>Generates a fresh authenticator key (not active until <see cref="EnableAsync"/> confirms a code from it). Refused while 2FA is already enabled.</summary>
        Task<ResultData<AuthenticatorSetupDto>> BeginSetupAsync(long userId);

        /// <summary>Turns 2FA on once the user proves their app produces valid codes for the key from <see cref="BeginSetupAsync"/>.</summary>
        Task<ResultData<bool>> EnableAsync(long userId, string? code);

        /// <summary>Turns 2FA off - requires a current code, so a stolen session alone can't swap in an attacker's authenticator.</summary>
        Task<ResultData<bool>> DisableAsync(long userId, string? code);

        /// <summary>Lost-device recovery: an admin, with their own current code, turns 2FA off for another user so they can set it up again.</summary>
        Task<ResultData<bool>> ResetForUserAsync(long adminUserId, string? adminCode, long targetUserId);

        /// <summary>
        /// The step-up check. Fails when the user hasn't enabled 2FA, the code is wrong, the code was already used, or
        /// too many wrong codes were tried recently. Each code is accepted once.
        /// </summary>
        Task<ResultData<bool>> VerifyCodeAsync(long userId, string? code);
    }
}
