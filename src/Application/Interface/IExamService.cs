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
        Task<ResultData<ExamPageDto>> GetExamPageAsync(long examId, string? secretKey);
    }
}
