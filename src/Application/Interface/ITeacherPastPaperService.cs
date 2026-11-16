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
    }
}
