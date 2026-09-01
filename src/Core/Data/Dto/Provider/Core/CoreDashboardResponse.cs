namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Raw shape of gama-api's GET /teachers/dashboard and GET /students/dashboard responses (undocumented in
    /// openapi.yaml beyond a generic envelope - field names below are reverse-engineered from what
    /// gamatrain-front's dashboard page actually reads, see app/pages/user/index.vue and the components it
    /// composes). Both endpoints share this shape; ExamSuggestions is only ever populated for students.
    /// </summary>
    public sealed class CoreDashboardResponse
    {
        [JsonPropertyName("user")]
        public UserDto? User { get; set; }

        [JsonPropertyName("profileCompletion")]
        public ProfileCompletionDto? ProfileCompletion { get; set; }

        [JsonPropertyName("unreadMessages")]
        public UnreadMessagesDto? UnreadMessages { get; set; }

        [JsonPropertyName("stats")]
        public StatsDto? Stats { get; set; }

        [JsonPropertyName("examSuggestions")]
        public ExamSuggestionsDto? ExamSuggestions { get; set; }

        public sealed class UserDto
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("username")]
            public string? Username { get; set; }

            [JsonPropertyName("first_name")]
            public string? FirstName { get; set; }

            [JsonPropertyName("last_name")]
            public string? LastName { get; set; }

            [JsonPropertyName("phone")]
            public string? Phone { get; set; }

            [JsonPropertyName("avatar")]
            public string? Avatar { get; set; }

            [JsonPropertyName("sex")]
            public string? Sex { get; set; }

            [JsonPropertyName("active")]
            public string? Active { get; set; }

            [JsonPropertyName("credit")]
            public string? Credit { get; set; }

            [JsonPropertyName("active_package")]
            public string? ActivePackage { get; set; }

            [JsonPropertyName("group_id")]
            public int? GroupId { get; set; }

            [JsonPropertyName("score")]
            public string? Score { get; set; }

            [JsonPropertyName("section")]
            public string? Section { get; set; }

            [JsonPropertyName("base")]
            public string? Base { get; set; }

            [JsonPropertyName("course")]
            public string? Course { get; set; }

            [JsonPropertyName("area")]
            public string? Area { get; set; }

            [JsonPropertyName("school")]
            public string? School { get; set; }

            [JsonPropertyName("score_check_info")]
            public string? ScoreCheckInfo { get; set; }

            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("city")]
            public string? City { get; set; }
        }

        public sealed class ProfileCompletionDto
        {
            [JsonPropertyName("total")]
            public int Total { get; set; }

            [JsonPropertyName("num")]
            public int Num { get; set; }

            [JsonPropertyName("notComplete")]
            public Collection<string>? NotComplete { get; set; }
        }

        public sealed class UnreadMessagesDto
        {
            [JsonPropertyName("total")]
            public int Total { get; set; }
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
