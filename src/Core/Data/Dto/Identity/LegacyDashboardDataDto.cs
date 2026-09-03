namespace GamaEdtech.Data.Dto.Identity
{
    using System.Collections.ObjectModel;

    /// <summary>
    /// The slice of identities/dashboard's response that still has no local equivalent and so is still sourced
    /// from gama-api - everything else (user identity/profile, profileCompletion, unreadMessages) is now built
    /// entirely from this backend's own data by IdentityService.GetDashboardAsync, which merges this DTO's
    /// fields into the final DashboardResponseDto.User rather than returning them separately. See
    /// docs/business/identity-and-access.md, "User dashboard proxy".
    /// </summary>
    public sealed class LegacyDashboardDataDto
    {
        /// <summary>
        /// False when gama-api couldn't be reached, returned an error, or the caller had no forwardable legacy
        /// token - every property below is then null. Never fails the overall identities/dashboard request.
        /// </summary>
        public bool LegacyDataAvailable { get; set; }

        /// <summary>
        /// True when gama-api rejected the caller's forwarded legacy token with HTTP 401/403 - propagated by
        /// IdentitiesController.GetDashboard as a real HTTP 401, unlike every other degrade case above. See
        /// DashboardResponseDto.LegacyAuthRejected's doc comment for the full reasoning.
        /// </summary>
        public bool LegacyAuthRejected { get; set; }

        public string? ScoreCheckInfo { get; set; }

        public StatsDto? Stats { get; set; }

        /// <summary>Only ever populated for students - null for teachers even when LegacyDataAvailable is true.</summary>
        public ExamSuggestionsDto? ExamSuggestions { get; set; }

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
