namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
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

        /// <summary>Daily job: deletes rows older than <c>UserActivity:RetentionDays</c> (default 365).</summary>
        Task<ResultData<int>> DeleteOldAsync();
    }
}
