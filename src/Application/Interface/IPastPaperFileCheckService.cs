namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// Checks the copy of past paper files: every <c>PastPaperFiles</c> row against the files stored in the private bucket's
    /// <c>PastPaper</c> container (gama-api's paths, unchanged). It records each file's size, which the paper page shows, and
    /// reports what is missing or unused. Reads the bucket and writes only <c>Size</c>/<c>CheckDate</c>. See
    /// docs/business/past-papers.md, "Checking the copy".
    /// </summary>
    [Injectable]
    public interface IPastPaperFileCheckService
    {
        /// <summary>Lists the whole container first (a failed listing changes nothing), then updates every row. Safe to run
        /// again, e.g. after the file copy is rerun.</summary>
        Task<ResultData<PastPaperFileCheckResultDto>> CheckAsync();
    }
}
