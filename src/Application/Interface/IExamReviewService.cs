namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamReview;

    /// <summary>
    /// Reviews of exams by the people who took them: a 1-5 rating and an optional comment, one per user and exam, public at once;
    /// an admin hides a bad one. See docs/business/exams-and-content.md, "Exam reviews".
    /// </summary>
    [Injectable]
    public interface IExamReviewService
    {
        /// <summary>The exam's rating summary and confirmed comments (newest first); with <paramref name="userId"/>, also their own
        /// review and whether they may write one.</summary>
        Task<ResultData<ExamReviewsDto>> GetReviewsAsync(long examId, long? userId, int page, int pageSize);

        /// <summary>Creates or changes the caller's review, public at once. Only for someone who finished the (public) exam:
        /// <c>notParticipated</c> otherwise. One an admin hid stays hidden.</summary>
        Task<ResultData<ExamReviewItemDto>> SaveReviewAsync([NotNull] SaveExamReviewRequestDto requestDto);

        Task<ResultData<bool>> DeleteReviewAsync(long userId, long examId);

        Task<ResultData<ExamReviewAdminListDto>> GetAdminReviewsAsync([NotNull] ExamReviewAdminListRequestDto requestDto);

        /// <summary>Shows (confirm) or hides a review (an admin's moderation); hiding keeps its note for the author.</summary>
        Task<ResultData<bool>> ModerateAsync(long id, bool confirm, string? rejectionComment);
    }
}
