namespace GamaEdtech.Data.Dto.Identity
{
    using System.Collections.ObjectModel;

    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// identities/dashboard's final, merged response. Phase 2 (2026-09-01): User/ProfileCompletion/
    /// UnreadMessages are built entirely from this backend's own data - always populated, independent of
    /// gama-api. Stats/ExamSuggestions (and the handful of User fields with no local equivalent -
    /// Section/Course/Area/ScoreCheckInfo) still have no local domain to source them from, so
    /// they stay proxied from gama-api and are the only parts LegacyDataAvailable/LegacyAuthRejected govern.
    /// See docs/business/identity-and-access.md, "User dashboard proxy".
    /// </summary>
    public sealed class DashboardResponseDto
    {
        /// <summary>
        /// False when gama-api couldn't be reached, returned an error, or the caller had no forwardable legacy
        /// token - Stats/ExamSuggestions and User's Section/Course/Area/ScoreCheckInfo are then
        /// null. Everything else on User, and ProfileCompletion/UnreadMessages, are unaffected - they're local.
        /// Never fails the overall request.
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
            // --- Local (always populated, independent of gama-api) ---
            public long? CoreId { get; set; }
            public string? Handle { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? AvatarUri { get; set; }
            public string? PhoneNumber { get; set; }
            public GenderType? Gender { get; set; }

            /// <summary>This app's own RBAC role names (e.g. "Teacher") - replaces gama-api's raw numeric Group signal.</summary>
            public IEnumerable<string>? Roles { get; set; }

            /// <summary>This backend's own points ledger (ApplicationUser.CurrentBalance) - the same value the leader-board endpoint ranks by. Not the same number as gama-api's own legacy "score".</summary>
            public long Points { get; set; }

            public bool Enabled { get; set; }
            public int? CityId { get; set; }
            public string? CityTitle { get; set; }
            public long? SchoolId { get; set; }
            public string? SchoolTitle { get; set; }

            /// <summary>Current subscription plan, if any - null on the free tier. Replaces gama-api's raw legacy "credit" field, which had no real local equivalent.</summary>
            public UserSubscriptionDto? Subscription { get; set; }

            // --- Still legacy-sourced - no local equivalent exists for these; null unless LegacyDataAvailable ---
            public string? Section { get; set; }
            public string? Course { get; set; }
            public string? Area { get; set; }
            public string? ScoreCheckInfo { get; set; }
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
