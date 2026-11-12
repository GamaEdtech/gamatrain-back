namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The past papers of <see cref="MigrationDomain.PastPapers"/>. Never call a source directly: go through
    /// <c>IMigrationRouter</c> (see <see cref="ITaxonomySource"/> for the rules a source follows). The calls that take a
    /// <c>SecretKey</c> act as that gama-api user; on failure the error carries gama-api's error code in <c>Reference</c>,
    /// its <c>reason</c> in <c>Info</c> and the HTTP status (when there was a response) in <c>Value</c>.
    /// </summary>
    [Injectable]
    public interface IPastPaperSource : IProvider<MigrationRoute>
    {
        /// <summary>A published past paper's id, title and slug, anonymously (<c>tests/{id}</c>, <c>Core:TestDetails</c>).</summary>
        Task<ResultData<ExamPaperLinkDto>> GetPaperLinkAsync(long paperId);

        /// <summary>gama-api's public paper list (<c>tests/search</c>, used by the subject directory), anonymously, with
        /// <paramref name="query"/> as its query string, and its <c>data</c> as gama-api sends it.</summary>
        Task<ResultData<JsonObject>> SearchPapersAsync([NotNull] string query);

        /// <summary>gama-api's public data of one past paper (<c>tests/{id}</c>), anonymously, and its <c>data</c> as gama-api sends it.</summary>
        Task<ResultData<JsonObject>> GetPaperAsync(long id);

        /// <summary><c>GET tests</c>: all papers for gama-api's admins and sub-admins, only the caller's own for a teacher.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto);

        /// <summary><c>GET search?type=test</c>: gamatrain's paper directory, the confirmed past papers whose title has
        /// <see cref="ExamImportPastPapersRequestDto.Title"/> (every one without it), newest session first, with whether
        /// each has an online exam.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> SearchPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto);

        /// <summary><c>GET tests/{id}</c>: a past paper with its files, each with whether it is free for the caller.</summary>
        Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary>
        /// <c>GET tests/download/{id}/{type}[/{extraId}]</c>: gama-api's temporary download link (about an hour) to one of a
        /// paper's files. One call per second (gama-api answers <c>gone</c> to more). It charges a caller who neither manages
        /// the paper nor has the file for free, so call it only for those, and route it as a write
        /// (<c>IMigrationRouter.RunWriteAsync</c>): it must never be repeated by the shadow comparison.
        /// </summary>
        Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] ExamImportPaperFileRequestDto requestDto);
    }
}
