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
        /// Resolves a gama-api download URL for PastPaper/Multimedia/Exam content (ContentType
        /// selects which gama-api endpoint - any other value, notably the historical
        /// ContentType.Test, is rejected as unsupported). If gama-api reports a price for this
        /// download (only PastPaper does) and it's not yet paid, charges the downloader
        /// (quota-then-points, same as GameService.SpendPointsAsync) and, only if that charge
        /// succeeds, accrues a commission to the content's owner (resolved from gama-api's CoreId,
        /// only reported for PastPaper). Multimedia/Exam downloads never charge or accrue
        /// commission, since gama-api reports neither a price nor an owner for those. Always
        /// returns the URL once gama-api's own lookup succeeds and any required charge succeeds.
        /// </summary>
        Task<ResultData<DownloadContentResponseDto>> DownloadContentAsync([NotNull] DownloadContentRequestDto requestDto);
    }
}
