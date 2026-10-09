namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Downloads a file from a link a user gave, through the public-network-only client (see
    /// <c>PublicNetworkHttpClient</c>).</summary>
    [Injectable]
    public interface IWebDownloadProvider
    {
        /// <summary>The file at <paramref name="url"/> (http or https), refused when it is larger than <paramref name="maxBytes"/>.
        /// A failure says why, in words for the user.</summary>
        Task<ResultData<byte[]>> DownloadAsync([NotNull] Uri url, int maxBytes, CancellationToken cancellationToken);
    }
}
