namespace GamaEdtech.Common.Identity
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.DataAnnotation;

    [Injectable]
    public interface ITokenService
    {
        Task<VerifyTokenResponse?> VerifyTokenAsync([NotNull] VerifyTokenRequest request);

        /// <summary>
        /// Temporary, part of the legacy-auth-bridge. Validates a gama-api (old backend) JWT directly - same
        /// signature-skipping issuer/audience/expiry check as the legacy-auth-bridge's sync step - and resolves it
        /// to the local user linked by CoreId, so a raw gama-api token works as an Authorization value with no
        /// gamatrain-back token minted at all. Returns null if the token isn't a valid legacy JWT or no local user
        /// is linked to it yet.
        /// </summary>
        Task<VerifyTokenResponse?> VerifyLegacyTokenAsync([NotNull] string token);
    }
}
