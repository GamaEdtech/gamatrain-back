namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// A past paper's files served from this backend (gama-api's <c>tests/download</c> rules, on the copy): which file a
    /// user asks for, its price for them, and a short-lived signed link to it in the private bucket. Charging is not done
    /// here: <c>ContentDeliveryService</c> charges through <c>IContentPurchaseService</c> between the two calls. See
    /// docs/business/past-papers.md, "Downloads".
    /// </summary>
    [Injectable]
    public interface IPastPaperDownloadService
    {
        /// <summary>The file (<paramref name="fileId"/>, <c>PastPaperFiles.Id</c> as the paper's <c>files</c> list gives it) of
        /// a paper the user may download: a published one, or any of theirs (or any, for an admin). Fails for anything else,
        /// or a file that is not stored.</summary>
        Task<ResultData<PastPaperDownloadDto>> GetFileAsync(long userId, long paperId, long fileId);

        /// <summary>A signed link to the file (a few minutes), saved under its download name. Counts a download unless the
        /// user is privileged. Call it only once the file is paid for or free.</summary>
        Task<ResultData<Uri>> GetLinkAsync(PastPaperDownloadDto file);

        /// <summary>Which of a paper's files the user owns, by file id: bought (<c>ContentPurchases</c>, also what was bought
        /// on gama-api), or all of them for the paper's owner or an admin.</summary>
        Task<ResultData<GetPaperOwnershipResponseDto>> GetOwnershipAsync(long userId, long paperId);

        /// <summary>gama-api's name for a file of the paper (<c>pdf</c>/<c>word</c>/<c>answer</c>, or <c>extra</c> and its id),
        /// for the routes that still ask gama-api; null when the paper has no such file.</summary>
        Task<(string Type, long? ExtraId)?> GetLegacyNameAsync(long paperId, long fileId);

        /// <summary>The ids of the paper's stored files of these kinds (gama-api reports ownership by kind).</summary>
        Task<IReadOnlyList<long>> GetFileIdsAsync(long paperId, IEnumerable<PastPaperFileKind> kinds);
    }
}
