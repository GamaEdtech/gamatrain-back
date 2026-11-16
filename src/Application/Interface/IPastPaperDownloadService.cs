namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;

    /// <summary>
    /// A past paper's files served from this backend (gama-api's <c>tests/download</c> rules, on the copy): which file a
    /// user asks for, its price for them, and a short-lived signed link to it in the private bucket. Charging is not done
    /// here: <c>ContentDeliveryService</c> charges through <c>IContentPurchaseService</c> between the two calls. See
    /// docs/business/past-papers.md, "Downloads".
    /// </summary>
    [Injectable]
    public interface IPastPaperDownloadService
    {
        /// <summary>The file (<paramref name="fileType"/> <c>pdf</c>, <c>word</c>, <c>answer</c> or <c>extra</c> with
        /// <paramref name="extraId"/>) of a paper the user may download: a published one, or any of theirs (or any, for an
        /// admin). Fails for anything else, or a file that is not stored.</summary>
        Task<ResultData<PastPaperDownloadDto>> GetFileAsync(long userId, long paperId, string? fileType, long? extraId);

        /// <summary>A signed link to the file (a few minutes), saved under its download name. Counts a download unless the
        /// user is privileged. Call it only once the file is paid for or free.</summary>
        Task<ResultData<Uri>> GetLinkAsync(PastPaperDownloadDto file);

        /// <summary>Which of a paper's question, Word and answer files the user owns: bought (<c>ContentPurchases</c>, also
        /// what was bought on gama-api), or all of them for the paper's owner or an admin.</summary>
        Task<ResultData<GetPaperOwnershipResponseDto>> GetOwnershipAsync(long userId, long paperId);
    }
}
