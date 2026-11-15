namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.UserActivity;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Reads and podcast listens of signed-in users. See docs/business/exams-and-content.md, "Reads and listens".</summary>
    [Injectable]
    public interface IUserActivityService
    {
        /// <summary>
        /// Records it once per user, kind, post and day: a repeat the same day is a success that changes nothing. Only for a
        /// published post (and, for the podcast kinds, one with a podcast).
        /// </summary>
        Task<ResultData<bool>> RecordAsync(long userId, UserActivityType type, long postId);

        /// <summary>Admin: the posts with the most readers in the last <paramref name="days"/> days (1-365), at most
        /// <paramref name="size"/> (1-100), with their readers, listeners and full listens (each a count of different users).</summary>
        Task<ResultData<IReadOnlyList<PostActivityDto>>> GetTopPostsAsync(int days, int size);

        /// <summary>Admin: one user's reads and listens in the last <paramref name="days"/> days (1-365), newest first, at most 200.</summary>
        Task<ResultData<IReadOnlyList<UserActivityItemDto>>> GetUserActivityAsync(long userId, int days);

        /// <summary>Daily job: deletes rows older than <c>UserActivity:RetentionDays</c> (default 365).</summary>
        Task<ResultData<int>> DeleteOldAsync();
    }
}
