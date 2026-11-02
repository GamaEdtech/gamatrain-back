namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Domain.Entity;

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

        /// <summary>Which of a past paper's pdf/word/answer files the token's user already owns (gama-api's own paid flags, the
        /// ones the download flow trusts), so the paper page can show them as owned before a click. Per user: never cached.</summary>
        Task<ResultData<GetPaperOwnershipResponseDto>> GetPaperOwnershipAsync([NotNull] string token, long paperId);

        /// <summary>
        /// Lists ContentOwnerCommission rows (every accrual; payouts are tracked separately, see
        /// ICommissionPayoutService). Callers are responsible for scoping the specification: a content owner's own
        /// report must include OwnerUserIdEqualsSpecification for the caller, while an admin report
        /// may omit it to see every owner.
        /// </summary>
        Task<ResultData<ListDataSource<ContentOwnerCommissionDto>>> GetContentOwnerCommissionsAsync(ListRequestDto<ContentOwnerCommission>? requestDto = null);

        /// <summary>
        /// Commission accrued to requestDto.UserId, bucketed by day-of-week or month (see Period) and summed as
        /// AmountUsd per bucket, plus the total across the whole [StartDate, EndDate] range. Same
        /// period/range shape and validation as ITransactionService.GetStatisticsAsync.
        /// </summary>
        Task<ResultData<GetCommissionStatisticsResponseDto>> GetCommissionStatisticsAsync([NotNull] GetCommissionStatisticsRequestDto requestDto);
    }
}
