namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Game;

    [Injectable]
    public interface IGameService
    {
        Task<ResultData<IEnumerable<CoinDto>>> EasterEggFortuneWheelAsync();
        Task<ResultData<long>> EasterEggPointsAsync([NotNull] EasterEggPointsRequestDto requestDto);
        Task<ResultData<SpendPointsResponseDto>> SpendPointsAsync([NotNull] SpendPointsRequestDto requestDto);
        Task<ResultData<TestTimeResponseDto>> TestTimeAsync([NotNull] TestTimeRequestDto requestDto);
        Task<ResultData<ExamPointsResponseDto>> ExamPointsAsync([NotNull] ExamPointsRequestDto requestDto);
    }
}
