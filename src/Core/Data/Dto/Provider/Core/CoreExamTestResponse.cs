namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json.Serialization;

    /// <summary>One question in full, from gama-api's <c>GET examTests?id={id}</c> (config <c>Core:ExamTest</c>,
    /// see <see cref="CoreExamTestListResponse"/>) -- including its correct option, which <c>exams/start</c> never
    /// returned. (The path form <c>examTests/{id}</c> refuses some questions with "permissionDenied" -- exam 1061's
    /// Q33 -- while this search returns them.)</summary>
    public sealed class CoreExamTestResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("answer_a")]
        public string? OptionA { get; set; }

        [JsonPropertyName("answer_b")]
        public string? OptionB { get; set; }

        [JsonPropertyName("answer_c")]
        public string? OptionC { get; set; }

        [JsonPropertyName("answer_d")]
        public string? OptionD { get; set; }

        [JsonPropertyName("q_file")]
        public string? QuestionFile { get; set; }

        [JsonPropertyName("a_file")]
        public string? OptionAFile { get; set; }

        [JsonPropertyName("b_file")]
        public string? OptionBFile { get; set; }

        [JsonPropertyName("c_file")]
        public string? OptionCFile { get; set; }

        [JsonPropertyName("d_file")]
        public string? OptionDFile { get; set; }

        /// <summary>The correct option, "1"-"4".</summary>
        [JsonPropertyName("true_answer")]
        public string? TrueAnswer { get; set; }

        /// <summary>The worked answer (rich text, may hold <c>$...$</c> formulas) -- what a descriptive question has
        /// instead of a correct option.</summary>
        [JsonPropertyName("answer_full")]
        public string? AnswerFull { get; set; }

        [JsonPropertyName("answer_full_file")]
        public string? AnswerFullFile { get; set; }

        [JsonPropertyName("answer_view_type")]
        public string? AnswerViewType { get; set; }

        [JsonPropertyName("testImgAnswers")]
        public bool TestImageAnswers { get; set; }
    }
}
