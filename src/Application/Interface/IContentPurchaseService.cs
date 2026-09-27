namespace GamaEdtech.Application.Interface
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Pay-once content (2026-09-27): charge a user for a piece of content once, then it's theirs. Shared by every kind
    /// of paid content (<see cref="PurchasableContentType"/>) -- today the exam export; a premium post would be another
    /// caller, not another table. Each caller keeps its own pricing rule and passes the price in.
    ///
    /// Flow: <see cref="ChargeAsync"/> (skips the charge if already owned or free) -> deliver the content ->
    /// <see cref="CompletePurchaseAsync"/>, or <see cref="RefundAsync"/> if delivery failed.
    /// </summary>
    [Injectable]
    public interface IContentPurchaseService
    {
        Task<ResultData<ContentChargeResponseDto>> ChargeAsync([NotNull] ContentPurchaseRequestDto requestDto);

        /// <summary>Records a charged purchase once the content was delivered. A concurrent duplicate purchase (the unique
        /// index rejects this row) refunds this request's own charge instead -- the user still paid once.</summary>
        Task CompletePurchaseAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge);

        /// <summary>Reverses a charge whose delivery failed. Best effort: a failed refund is logged, never thrown.</summary>
        Task RefundAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge);

        /// <summary>Which variants of one piece of content the user owns ("" for content with a single form).</summary>
        Task<ResultData<IReadOnlyCollection<string>>> GetPurchasedVariantsAsync(long userId, PurchasableContentType contentType, long contentId);
    }
}
