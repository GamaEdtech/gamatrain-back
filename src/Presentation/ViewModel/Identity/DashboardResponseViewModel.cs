namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using System.Collections.ObjectModel;

    public sealed class DashboardResponseViewModel
    {
        /// <summary>
        /// False when gama-api couldn't be reached for this caller (e.g. no forwardable legacy token, or gama-api
        /// itself errored) - every other property is then null. The request itself still succeeds; the frontend
        /// should render an empty/skeleton state for the affected widgets rather than treat this as an error.
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
