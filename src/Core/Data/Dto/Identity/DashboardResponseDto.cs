namespace GamaEdtech.Data.Dto.Identity
{
    using System.Collections.ObjectModel;

    /// <summary>
    /// Phase 0 of the identities/dashboard proxy: a field-for-field passthrough of gama-api's teacher/student
    /// dashboard response (see CoreDashboardResponse), with nothing added yet from this backend's own data. See
    /// docs/business/identity-and-access.md, "User dashboard proxy".
    /// </summary>
    public sealed class DashboardResponseDto
    {
        /// <summary>
        /// False when gama-api couldn't be reached, returned an error, or the caller had no forwardable legacy
        /// token - every property below is then null. This never fails the overall request (still Succeeded) -
        /// see docs/business/identity-and-access.md.
        /// </summary>
        public bool LegacyDataAvailable { get; set; }

        /// <summary>
        /// True when gama-api rejected the caller's forwarded legacy token with HTTP 401/403 - i.e. this
        /// backend's own auth already accepted the token (it's cryptographically valid and unexpired), but
        /// gama-api itself no longer honors it (e.g. the session was ended via gama-api's own logout, or the
        /// account was disabled, directly on gama-api's side). Unlike every other legacy failure mode, this one
        /// is NOT swallowed into LegacyDataAvailable = false - IdentitiesController.GetDashboard propagates it
        /// as a real HTTP 401 instead, so gamatrain-front's existing global 401/403 interceptor
        /// (useApiService.ts) re-authenticates the user, same as it already does for every other endpoint. See
        /// docs/business/identity-and-access.md, "User dashboard proxy".
        /// </summary>
        public bool LegacyAuthRejected { get; set; }

        public UserDto? User { get; set; }
        public ProfileCompletionDto? ProfileCompletion { get; set; }
        public UnreadMessagesDto? UnreadMessages { get; set; }
        public StatsDto? Stats { get; set; }

        /// <summary>Only ever populated for students - null for teachers even when LegacyDataAvailable is true.</summary>
        public ExamSuggestionsDto? ExamSuggestions { get; set; }

        public sealed class UserDto
        {
            public string? Id { get; set; }
            public string? Username { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? Phone { get; set; }
            public string? Avatar { get; set; }
            public string? Sex { get; set; }
            public string? Active { get; set; }
            public string? Credit { get; set; }
            public string? ActivePackage { get; set; }
            public int? GroupId { get; set; }
            public string? Score { get; set; }
            public string? Section { get; set; }
            public string? Base { get; set; }
            public string? Course { get; set; }
            public string? Area { get; set; }
            public string? School { get; set; }
            public string? ScoreCheckInfo { get; set; }
            public string? State { get; set; }
            public string? City { get; set; }
        }

        public sealed class ProfileCompletionDto
        {
            public int Total { get; set; }
            public int Num { get; set; }
            public Collection<string>? NotComplete { get; set; }
        }

        public sealed class UnreadMessagesDto
        {
            public int Total { get; set; }
        }

        public sealed class StatsDto
        {
            public StatItemDto? Test { get; set; }
            public StatItemDto? File { get; set; }
            public StatItemDto? Question { get; set; }
        }

        public sealed class StatItemDto
        {
            public int Total { get; set; }
        }

        public sealed class ExamSuggestionsDto
        {
            public int Total { get; set; }
            public int Participated { get; set; }
            public Collection<LessonDto>? Lessons { get; set; }
        }

        public sealed class LessonDto
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public int Participated { get; set; }
            public int Total { get; set; }
        }
    }
}
