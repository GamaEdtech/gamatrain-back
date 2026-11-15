namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// Copies gama-api's past papers (<c>tests</c>) and their extra files (<c>tests_extra_files</c>) into <c>PastPapers</c>
    /// and <c>PastPaperFiles</c>, reading gama-api's database read-only. Nothing reads the copy yet: the past paper
    /// endpoints keep going to gama-api until the domain is switched in its own change. See docs/business/past-papers.md.
    /// </summary>
    [Injectable]
    public interface IPastPaperImportService
    {
        /// <summary>
        /// Reads every paper and extra file and adds or updates its copy (never duplicates, never deletes a paper), linking
        /// each owner to the user here with that <c>CoreId</c>. Safe to run again: each run brings the copy up to date.
        /// <c>NotValid</c> when <c>Migration:LegacyDatabase:ConnectionString</c> is not set. With
        /// <c>Migration:LegacyDatabase:MaxPastPapers</c> set (a test server), only that many papers are copied, in id order,
        /// with their files; extra files of the papers left out are skipped.
        /// </summary>
        Task<ResultData<PastPaperImportResultDto>> ImportAsync();
    }
}
