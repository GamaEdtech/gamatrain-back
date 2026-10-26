namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// gama-api's <c>exams/result/{id}</c> read in full: the student's own result with every question, its options,
    /// correct answer and the student's answer. Kept apart from <see cref="CoreExamResultResponse"/> (only the totals)
    /// so a field gama-api sends in an unexpected shape can't break exam submission points. Values gama-api sends as
    /// either text or a number (or "" for none), and <c>rank</c> (an empty <c>[]</c> when there is none), are read as
    /// <see cref="JsonElement"/>.
    /// </summary>
    public sealed class CoreExamResultDetailResponse
    {
        [JsonPropertyName("exam")]
        public ExamDto? Exam { get; set; }

        [JsonPropertyName("userData")]
        public UserDataDto? UserData { get; set; }

        [JsonPropertyName("rank")]
        public JsonElement? Rank { get; set; }

        [JsonPropertyName("answerStats")]
        public CoreExamResultResponse.AnswerStatsDto? AnswerStats { get; set; }

        [JsonPropertyName("result")]
        public IReadOnlyList<QuestionDto>? Result { get; set; }

        public sealed class ExamDto
        {
            [JsonPropertyName("title")]
            public JsonElement? Title { get; set; }

            [JsonPropertyName("azmoon_time")]
            public JsonElement? ExamTime { get; set; }
        }

        public sealed class UserDataDto
        {
            /// <summary>Seconds the student took, as text ("2700"); 65535 is gama-api's cap, not a real time. (Its
            /// <c>submit_time_seconds</c> is the same time written out in Persian, not a number.)</summary>
            [JsonPropertyName("submit_time")]
            public JsonElement? SubmitTime { get; set; }
        }

        public sealed class QuestionDto
        {
            [JsonPropertyName("question")]
            public JsonElement? Question { get; set; }

            [JsonPropertyName("q_file")]
            public JsonElement? QuestionFile { get; set; }

            [JsonPropertyName("type")]
            public JsonElement? Type { get; set; }

            [JsonPropertyName("answer_a")]
            public JsonElement? OptionA { get; set; }

            [JsonPropertyName("answer_b")]
            public JsonElement? OptionB { get; set; }

            [JsonPropertyName("answer_c")]
            public JsonElement? OptionC { get; set; }

            [JsonPropertyName("answer_d")]
            public JsonElement? OptionD { get; set; }

            [JsonPropertyName("a_file")]
            public JsonElement? OptionAFile { get; set; }

            [JsonPropertyName("b_file")]
            public JsonElement? OptionBFile { get; set; }

            [JsonPropertyName("c_file")]
            public JsonElement? OptionCFile { get; set; }

            [JsonPropertyName("d_file")]
            public JsonElement? OptionDFile { get; set; }

            /// <summary>1-4 for option A-D.</summary>
            [JsonPropertyName("true_answer")]
            public JsonElement? TrueAnswer { get; set; }

            /// <summary>1-4 for option A-D; 0 or empty when unanswered.</summary>
            [JsonPropertyName("user_answer")]
            public JsonElement? UserAnswer { get; set; }

            [JsonPropertyName("lesson_title")]
            public JsonElement? LessonTitle { get; set; }

            [JsonPropertyName("topics_title")]
            public JsonElement? TopicsTitle { get; set; }
        }
    }
}
