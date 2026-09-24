namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    /// <summary>gama-api's <c>GET exams/{id}</c> (config <c>Core:Exam</c>): the exam's own details plus the ordered
    /// ids of its questions, each fetched in full from <c>Core:ExamTest</c> (<see cref="CoreExamTestResponse"/>).</summary>
    public sealed class CoreExamResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("azmoon_type")]
        public string? ExamType { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>The exam's question ids, in exam order.</summary>
        [JsonPropertyName("tests")]
        public Collection<string>? Tests { get; set; }

        [JsonPropertyName("tests_num")]
        public string? TestsCount { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("azmoon_time")]
        public string? ExamTime { get; set; }

        /// <summary>Difficulty: "1" easy, "2" medium, "3" hard.</summary>
        [JsonPropertyName("level")]
        public string? Level { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>The exam author's gama-api user id -- our <c>ApplicationUser.CoreId</c>.</summary>
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }
    }
}
