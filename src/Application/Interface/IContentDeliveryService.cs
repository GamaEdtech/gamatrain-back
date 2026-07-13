namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Content;

    [Injectable]
    public interface IContentDeliveryService
    {
        /// <summary>
        /// Resolves a gama-api test-file download URL. If gama-api reports the download as not yet
        /// paid, charges the downloader (quota-then-points, same as GameService.SpendPointsAsync)
        /// and, only if that charge succeeds, accrues a commission to the content's owner (resolved
        /// from gama-api's CoreId). Always returns the URL when gama-api's own lookup succeeds and
        /// the downloader's charge (if any) succeeds.
        /// </summary>
        Task<ResultData<DownloadContentResponseDto>> DownloadTestAsync([NotNull] DownloadTestRequestDto requestDto);
    }
}
