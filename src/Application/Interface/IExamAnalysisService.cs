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
        Task<ResultData<ExamAnalysisDto>> SaveResultAsync([NotNull] SaveExamAnalysisResultRequestDto requestDto);
    }
}
