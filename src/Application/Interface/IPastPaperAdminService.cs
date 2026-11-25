namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// The admin panel's past papers: every paper from this backend's copy, as gama-api's admins see them. Reading works at any
    /// time; changing them (adding for a teacher, editing, deleting, reviewing, scoring) only once
    /// <c>Migration:WriteRoutes:PastPapers.Teacher</c> is <c>Native</c>, since until then gama-api is where papers change.
    /// </summary>
    [Injectable]
    public interface IPastPaperAdminService
    {
        /// <summary>Every paper that is not deleted, newest first, with gama-api's list filters plus <c>uid</c> (owner),
        /// <c>refScore</c> and <c>title</c>.</summary>
        Task<ResultData<JsonObject>> GetPapersAsync(long userId, string? query);

        /// <summary>One paper in any status, with full edit rights and the review's notes.</summary>
        Task<ResultData<JsonObject>> GetPaperAsync(long userId, long id);

        /// <summary>A short signed link to one of a paper's files (<c>pdf</c>, <c>word</c>, <c>answer</c>, <c>extra</c> with its
        /// id), in any status, for the review. Nothing is charged or counted. Answers <c>{"url"}</c>.</summary>
        Task<ResultData<JsonObject>> GetFileLinkAsync(long id, string? fileType, long? extraId, bool pending = false);

        Task<ResultData<JsonNode>> UploadAsync([NotNull] TeacherPaperUploadDto requestDto);

        /// <summary>Adds a paper for a teacher (<c>uid</c> in the body).</summary>
        Task<ResultData<JsonNode>> AddAsync([NotNull] TeacherPaperRequestDto requestDto);

        Task<ResultData<JsonNode>> EditAsync([NotNull] TeacherPaperRequestDto requestDto);

        Task<ResultData<JsonNode>> DeleteAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>Sets a paper's status (confirm, reject, ask for changes, deactivate, back to waiting) with notes for the
        /// teacher. Answers <c>{"id", "status"}</c>.</summary>
        Task<ResultData<JsonObject>> ReviewAsync([NotNull] PastPaperReviewDto requestDto);

        /// <summary>Sets a paper's referee score, 0 to 5. Answers <c>{"id", "ref_score"}</c>.</summary>
        Task<ResultData<JsonObject>> SetScoreAsync(long id, byte score);
    }
}
