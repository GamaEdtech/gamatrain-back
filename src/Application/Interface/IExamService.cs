namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.Game;

    [Injectable]
    public interface IExamService
    {
        Task<ResultData<ExportExamResponseDto>> ExportExamAsync([NotNull] ExportExamRequestDto requestDto);

        /// <summary>The exam's price per paid export format for <paramref name="userId"/>, and which of them they already
        /// bought -- for showing prices before anything is generated.</summary>
        Task<ResultData<ExportPricesResponseDto>> GetExportPricesAsync(long userId, long examId, string? secretKey);

        /// <summary>Everything the public exam page shows, in one call: gama-api's exam details (live, with the caller's own
        /// fields when <paramref name="secretKey"/> is given), a few sample questions without their answers, and the past
        /// paper it was made from. The samples and paper are cached per exam; a failure there never fails the page.</summary>
        Task<ResultData<ExamPageDto>> GetExamPageAsync(long examId, string? secretKey, long? userId = null);

        /// <summary>Starts (or resumes) the caller's attempt at an exam: gama-api's <c>exams/start/{id}</c> data, from whichever side
        /// writes results (<c>WriteRoutes:Exams.Games</c>).</summary>
        Task<ResultData<System.Text.Json.Nodes.JsonObject>> StartExamAsync(long userId, long examId, string? secretKey);

        /// <summary>The caller's own results, newest first (gama-api's <c>exams/results</c>).</summary>
        Task<ResultData<System.Text.Json.Nodes.JsonObject>> GetUserResultsAsync(long userId, string? secretKey, string? query);

        /// <summary>An exam's participants, for its owner (gama-api's <c>exams/participants/{id}</c>).</summary>
        Task<ResultData<System.Text.Json.Nodes.JsonObject>> GetParticipantsAsync(long userId, long examId, string? secretKey, string? query);
    }
}
