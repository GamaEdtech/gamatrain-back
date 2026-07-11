namespace GamaEdtech.Common.Identity
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.DataAnnotation;

    [Injectable]
    public interface ITokenService
    {
        Task<VerifyTokenResponse?> VerifyTokenAsync([NotNull] VerifyTokenRequest request);
    }
}
