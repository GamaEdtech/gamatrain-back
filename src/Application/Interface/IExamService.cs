namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Game;

    [Injectable]
    public interface IExamService
    {
        Task<ResultData<ExportExamResponseDto>> ExportExamAsync([NotNull] ExportExamRequestDto requestDto);

        /// <summary>The exam's price per paid export format for <paramref name="userId"/>, and which of them they already
        /// bought -- for showing prices before anything is generated.</summary>
        Task<ResultData<ExportPricesResponseDto>> GetExportPricesAsync(long userId, long examId, string? secretKey);
    }
}
