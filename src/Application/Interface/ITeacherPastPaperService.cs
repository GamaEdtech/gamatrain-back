namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>A teacher's own past papers, from gama-api or this backend as <c>Migration:Routes:PastPapers.Teacher</c> says.
    /// Answers are gama-api's, as it sends them. Never cached: each is the caller's own.</summary>
    [Injectable]
    public interface ITeacherPastPaperService
    {
        /// <summary>The caller's papers (gama-api's <c>GET tests</c> for a teacher).</summary>
        Task<ResultData<JsonObject>> GetPapersAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>One paper for the edit form (gama-api's <c>GET tests/{id}?for=edit</c>).</summary>
        Task<ResultData<JsonObject>> GetPaperAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>Uploads one of a paper's files (gama-api's <c>POST upload</c>); the add or edit names it by the key it answers.
        /// The uploads, adds, edits and deletes follow <c>Migration:WriteRoutes:PastPapers.Teacher</c>.</summary>
        Task<ResultData<JsonNode>> UploadAsync([NotNull] TeacherPaperUploadDto requestDto);

        /// <summary>Adds a paper for the caller (gama-api's <c>POST tests</c>).</summary>
        Task<ResultData<JsonNode>> AddAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>Edits the caller's paper (gama-api's <c>PUT tests/{id}</c>).</summary>
        Task<ResultData<JsonNode>> EditAsync([NotNull] TeacherPaperRequestDto requestDto);

        /// <summary>Deletes the caller's paper or one of its files (gama-api's <c>DELETE tests/{id}[/{type}]</c>).</summary>
        Task<ResultData<JsonNode>> DeleteAsync([NotNull] TeacherPaperRequestDto requestDto);
    }
}
