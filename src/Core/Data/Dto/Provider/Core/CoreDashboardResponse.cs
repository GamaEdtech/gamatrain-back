namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Raw shape of gama-api's GET /teachers/dashboard and GET /students/dashboard responses (undocumented in
    /// openapi.yaml beyond a generic envelope - field names below are reverse-engineered from what
    /// gamatrain-front's dashboard page actually reads, see app/pages/user/index.vue and the components it
    /// composes). Both endpoints share this shape; ExamSuggestions is only ever populated for students.
    ///
    /// Phase 2 (2026-09-01): only the fields with no local equivalent are still read from here -
    /// user/profileCompletion/unreadMessages are now built entirely from this backend's own data
    /// (IdentityService.GetDashboardAsync); Stats and ExamSuggestions have no local domain to source them from
    /// yet, and the User sub-object here is trimmed to score_check_info, the one remaining field with no local
    /// equivalent (section/course/area were replaced by local Board/Grade - same fields `profiles` GET already
    /// returns - once it became clear they're this backend's real equivalent of gama-api's curriculum-board/
    /// grade-level signal) - see docs/business/identity-and-access.md, "User dashboard proxy".
    /// </summary>
    public sealed class CoreDashboardResponse
    {
        [JsonPropertyName("user")]
        public UserDto? User { get; set; }

        [JsonPropertyName("stats")]
        public StatsDto? Stats { get; set; }

        [JsonPropertyName("examSuggestions")]
        public ExamSuggestionsDto? ExamSuggestions { get; set; }

        public sealed class UserDto
        {
            [JsonPropertyName("score_check_info")]
            public string? ScoreCheckInfo { get; set; }
        }

        public sealed class StatsDto
        {
            [JsonPropertyName("test")]
            public StatItemDto? Test { get; set; }

            [JsonPropertyName("file")]
            public StatItemDto? File { get; set; }

            [JsonPropertyName("question")]
            public StatItemDto? Question { get; set; }
        }

        public sealed class StatItemDto
        {
            [JsonPropertyName("total")]
            public int Total { get; set; }
        }

        public sealed class ExamSuggestionsDto
        {
            [JsonPropertyName("total")]
            public int Total { get; set; }

            [JsonPropertyName("participated")]
            public int Participated { get; set; }

            [JsonPropertyName("lessons")]
            public Collection<LessonDto>? Lessons { get; set; }
        }

        public sealed class LessonDto
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("participated")]
            public int Participated { get; set; }

            [JsonPropertyName("total")]
            public int Total { get; set; }
        }
    }
}
