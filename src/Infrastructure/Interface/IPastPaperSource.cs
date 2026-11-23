namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.PastPaper;
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

        /// <summary>The papers related to a page's item (<paramref name="source"/> <c>test</c>, <c>exam</c> or <c>tutorial</c>),
        /// for its "Related Past Papers" row: gama-api's <c>recommendations/related</c> <c>tests</c>, anonymously, as gama-api
        /// sends them.</summary>
        Task<ResultData<JsonArray>> GetRelatedPapersAsync([NotNull] string source, long id);

        /// <summary>gama-api's <c>GET tests</c> for a teacher: the caller's own papers (newest first, also those an admin
        /// deleted), with <see cref="TeacherPaperRequestDto.Query"/> as its query string, and its <c>data</c> as gama-api sends it.</summary>
        Task<ResultData<JsonObject>> GetOwnPapersAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>gama-api's <c>GET tests/{id}?for=edit</c> for a paper's owner: the paper in any status with how the owner may
        /// edit it (<c>edit</c>), for the edit form. It counts no view. Someone else's unpublished paper is denied.</summary>
        Task<ResultData<JsonObject>> GetOwnPaperAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>gama-api's <c>POST upload</c> for a paper's file: <c>[{"file": {"name": key}}]</c>, the key the add or edit then
        /// names the file by. A write: route it with <c>IMigrationRouter.RunWriteAsync</c>, as the add and edit that use the key.</summary>
        Task<ResultData<JsonNode>> UploadPaperFileAsync([NotNull] TeacherPaperUploadDto requestDto);

        /// <summary>gama-api's <c>POST tests</c>: adds a paper for the caller from <see cref="TeacherPaperRequestDto.Body"/>, waiting
        /// for review; answers <c>{"id"}</c>. A write.</summary>
        Task<ResultData<JsonNode>> AddPaperAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>gama-api's <c>PUT tests/{id}</c>: edits the caller's paper (everything while it waits for review, then only
        /// the description and files, and it goes back to review); answers <c>{"id", "status", "editMode"}</c>. A write.</summary>
        Task<ResultData<JsonNode>> EditPaperAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>gama-api's <c>DELETE tests/{id}[/{type}]</c>: deletes the caller's paper, or one of its files, until it is
        /// past review. A write.</summary>
        Task<ResultData<JsonNode>> DeletePaperAsync([NotNull] TeacherPaperRequestDto requestDto);

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
