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
        /// <summary>
        /// Records a finished online exam (<c>ExamSubmission</c>) and gives its points, once per user and exam; a second call is
        /// a <c>Duplicate</c>. Only the exam result proxy calls it, with the result it just
        /// fetched from gama-api, so the browser can't skip or fake it. See docs/business/exams-and-content.md.
        /// </summary>
        Task<ResultData<ExamPointsResponseDto>> RecordExamSubmissionAsync(long userId, long examId, [NotNull] ExamResultDetailResponseDto result, int? timeSpentSeconds);

        /// <summary>
        /// The exam submit proxy: forwards the student's submit to gama-api unchanged and, once it is accepted, records the exam
        /// with its points and the time the student's countdown spent (<see cref="RecordExamSubmissionAsync"/>, at most the exam's
        /// duration). <c>Data</c> is gama-api's result id, also when it refuses an exam already submitted. Recording never fails the
        /// submit. See docs/business/exams-and-content.md, "Time spent on an exam".
        /// </summary>
        Task<ResultData<long?>> EndExamAsync(long userId, [NotNull] Data.Dto.Exam.ExamEndRequestDto requestDto, int? timeSpentSeconds);
    }
}
