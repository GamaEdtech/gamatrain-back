namespace GamaEdtech.Data.Dto.Identity
{
    using System.Collections.ObjectModel;

    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// identities/dashboard's response, built entirely from this backend's own tables (since 2026-11-18; until then
    /// Stats/ExamSuggestions came from gama-api). See docs/business/identity-and-access.md, "User dashboard".
    /// </summary>
    public sealed class DashboardResponseDto
    {
        public UserDto? User { get; set; }
        public ProfileCompletionDto? ProfileCompletion { get; set; }
        public UnreadMessagesDto? UnreadMessages { get; set; }
        public StatsDto? Stats { get; set; }

        /// <summary>The published online exams of the subjects of the user's grade, and how many of them the user finished.</summary>
        public ExamSuggestionsDto? ExamSuggestions { get; set; }

        public sealed class UserDto
        {
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

            /// <summary>Curriculum board (e.g. Cambridge) - replaces gama-api's raw legacy "section" field.</summary>
            public int? Board { get; set; }

            /// <summary>Grade/class level - replaces gama-api's raw legacy "course" field.</summary>
            public int? Grade { get; set; }

            /// <summary>Current subscription plan, if any - null on the free tier. Replaces gama-api's raw legacy "credit" field, which had no real local equivalent.</summary>
            public UserSubscriptionDto? Subscription { get; set; }
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
            /// <summary>The past papers the user owns.</summary>
            public StatItemDto? Test { get; set; }

            /// <summary>The exams the user owns.</summary>
            public StatItemDto? Exam { get; set; }
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
