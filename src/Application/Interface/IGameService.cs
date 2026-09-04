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

        /// <summary>
        /// Reverses a previously-successful <see cref="SpendPointsAsync"/> charge - for a caller that charged the
        /// user for something it then failed to actually deliver (e.g. <c>ContentDeliveryService</c>, when
        /// gama-api's own download call fails after the local charge already succeeded). Branches on
        /// <see cref="RefundPointsRequestDto.PaidBy"/>: a <see cref="Domain.Enumeration.SpendSource.SubscriptionQuota"/>
        /// charge credits quota back via <c>ISubscriptionQuotaService.RefundQuotaAsync</c> (requires
        /// <see cref="RefundPointsRequestDto.UserSubscriptionId"/> - see <see cref="SpendPointsResponseDto.
        /// CurrentSubscriptionId"/>'s doc), a <see cref="Domain.Enumeration.SpendSource.Points"/> charge credits
        /// the wallet back via <c>ITransactionService.IncreaseBalanceAsync</c>.
        /// </summary>
        Task<ResultData<bool>> RefundPointsAsync([NotNull] RefundPointsRequestDto requestDto);
        Task<ResultData<TestTimeResponseDto>> TestTimeAsync([NotNull] TestTimeRequestDto requestDto);
        Task<ResultData<ExamPointsResponseDto>> ExamPointsAsync([NotNull] ExamPointsRequestDto requestDto);
    }
}
