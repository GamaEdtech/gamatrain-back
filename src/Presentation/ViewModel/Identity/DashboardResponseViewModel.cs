namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    public sealed class DashboardResponseViewModel
    {
        /// <summary>
        /// False when gama-api couldn't be reached for this caller - Stats/ExamSuggestions and
        /// User.ScoreCheckInfo are then null. Everything else on User, and ProfileCompletion/UnreadMessages,
        /// are unaffected - they're built from this backend's own data. The request itself still succeeds
        /// either way.
        /// </summary>
        public bool LegacyDataAvailable { get; set; }

        public UserViewModel? User { get; set; }
        public ProfileCompletionViewModel? ProfileCompletion { get; set; }
        public UnreadMessagesViewModel? UnreadMessages { get; set; }
        public StatsViewModel? Stats { get; set; }

        /// <summary>Only ever populated for students - null for teachers even when LegacyDataAvailable is true.</summary>
        public ExamSuggestionsViewModel? ExamSuggestions { get; set; }

        public sealed class UserViewModel
        {
            public long? CoreId { get; set; }
            public string? Handle { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? AvatarUri { get; set; }
            public string? PhoneNumber { get; set; }

            [JsonConverter(typeof(EnumerationConverter<GenderType, byte>))]
            public GenderType? Gender { get; set; }

            /// <summary>This app's own RBAC role names (e.g. "Teacher") - replaces gama-api's raw numeric Group signal.</summary>
            public IEnumerable<string>? Roles { get; set; }

            /// <summary>This backend's own points ledger - the same value the leader-board endpoint ranks by. Not the same number as gama-api's own legacy "score".</summary>
            public long Points { get; set; }

            public bool Enabled { get; set; }
            public int? CityId { get; set; }
            public string? CityTitle { get; set; }
            public long? SchoolId { get; set; }
            public string? SchoolTitle { get; set; }

            /// <summary>Curriculum board (e.g. Cambridge) - replaces gama-api's raw legacy "section" field.</summary>
            public int? Board { get; set; }

            /// <summary>Grade/class level - replaces gama-api's raw legacy "course" field.</summary>
            public int? Grade { get; set; }

            /// <summary>Current subscription plan, if any - null on the free tier. Replaces gama-api's raw legacy "credit" field, which had no real local equivalent.</summary>
            public UserSubscriptionResponseViewModel? Subscription { get; set; }

            // Still legacy-sourced - no local equivalent exists for this; null unless LegacyDataAvailable.
            public string? ScoreCheckInfo { get; set; }
        }

        public sealed class ProfileCompletionViewModel
        {
            public int Total { get; set; }
            public int Num { get; set; }
            public Collection<string>? NotComplete { get; set; }
        }

        public sealed class UnreadMessagesViewModel
        {
            public int Total { get; set; }
        }

        public sealed class StatsViewModel
        {
            public StatItemViewModel? Test { get; set; }
            public StatItemViewModel? File { get; set; }
            public StatItemViewModel? Question { get; set; }
        }

        public sealed class StatItemViewModel
        {
            public int Total { get; set; }
        }

        public sealed class ExamSuggestionsViewModel
        {
            public int Total { get; set; }
            public int Participated { get; set; }
            public Collection<LessonViewModel>? Lessons { get; set; }
        }

        public sealed class LessonViewModel
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public int Participated { get; set; }
            public int Total { get; set; }
        }
    }
}
