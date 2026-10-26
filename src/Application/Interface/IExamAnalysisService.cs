namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamAnalysis;

    [Injectable]
    public interface IExamAnalysisService
    {
        Task<ResultData<PrepareExamAnalysisResponseDto>> PrepareAsync([NotNull] PrepareExamAnalysisRequestDto requestDto);

        /// <summary>The caller's saved analysis of an exam, or its price when there is none. Never charges or calls
        /// gama-api or the AI.</summary>
        Task<ResultData<ExamAnalysisStatusDto>> GetStatusAsync(long userId, long examId);

        /// <summary>The results page: the caller's gama-api result (<paramref name="secretKey"/> is their gama-api token) with
        /// their analysis status attached. Never charges.</summary>
        Task<ResultData<ExamResultPageDto>> GetResultPageAsync(long userId, long examId, string secretKey);
    }
}
