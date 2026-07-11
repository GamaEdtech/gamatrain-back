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
        /// Temporary, part of the legacy-auth-bridge. If <paramref name="token"/> is a valid signed composite
        /// envelope (see CompositeTokenEnvelope), returns the embedded gamatrain-back token; otherwise returns
        /// null and the caller should treat <paramref name="token"/> as a plain, non-enveloped token.
        /// </summary>
        string? UnwrapCompositeToken([NotNull] string token);
    }
}
