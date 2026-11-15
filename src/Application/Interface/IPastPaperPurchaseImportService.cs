namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// Copies the past paper files users bought on gama-api, so nobody pays for a file twice after the move. Each paid
    /// row of gama-api's <c>payments</c> for a paper file becomes a <c>LegacyPastPaperPurchases</c> copy and, once its
    /// paper is copied and its buyer has an account here, a <c>ContentPurchases</c> row (<c>PastPaper</c>, paid by
    /// <c>GamaApi</c>). Nothing reads those rows until past papers are switched off the proxy. See
    /// docs/business/past-papers.md, "Purchases made on gama-api".
    /// </summary>
    [Injectable]
    public interface IPastPaperPurchaseImportService
    {
        /// <summary>
        /// Reads gama-api's payments (only the columns ownership needs, never card or IP data), copies the paid past
        /// paper files, then runs <see cref="LinkAsync"/>. Safe to run again. <c>NotValid</c> without
        /// <c>Migration:LegacyDatabase:ConnectionString</c>.
        /// </summary>
        Task<ResultData<PastPaperPurchaseImportResultDto>> ImportAsync();

        /// <summary>
        /// Turns every copied purchase whose paper and buyer are both here into its ownership row; the rest wait for a
        /// later run. Reads only this backend's database.
        /// </summary>
        Task<ResultData<PastPaperPurchaseImportResultDto>> LinkAsync();
    }
}
