namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// The past paper base price list (<c>PastPaperFilePrices</c>), managed by admins here since 2026-11-26: the base price
    /// per file extension that <c>PastPaperPricing</c> starts from. An extension with no row is free. See
    /// docs/business/past-papers.md, "Prices".
    /// </summary>
    [Injectable]
    public interface IPastPaperFilePriceService
    {
        /// <summary>Every row, by id.</summary>
        Task<ResultData<IReadOnlyList<PastPaperFilePriceDto>>> GetPricesAsync();

        /// <summary>Adds (no id) or edits a row. Each extension may be priced by one row only. Answers the row's id.</summary>
        Task<ResultData<long>> ManagePriceAsync([NotNull] ManagePastPaperFilePriceRequestDto requestDto);

        /// <summary>Deletes a row: its extensions become free.</summary>
        Task<ResultData<bool>> DeletePriceAsync(long id);
    }
}
