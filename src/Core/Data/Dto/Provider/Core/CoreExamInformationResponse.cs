namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    public sealed class CoreExamInformationResponse
    {
        [JsonPropertyName("startID")]
        public int? StartId { get; set; }

        [JsonPropertyName("admin")]
        public bool Admin { get; set; }

        [JsonPropertyName("referee")]
        public bool Referee { get; set; }

        [JsonPropertyName("remainedSeconds")]
        public long? RemainedSeconds { get; set; }

        [JsonPropertyName("exam")]
        public ExamDto? Exam { get; set; }

        [JsonPropertyName("tests")]
        public Collection<TestDto>? Tests { get; set; }

        public sealed class ExamDto
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("user_")]
            public string? User { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("azmoon_type")]
            public string? ExamType { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("tests")]
            public string? Tests { get; set; }

            [JsonPropertyName("tests_num")]
            public string? TestsCount { get; set; }

            [JsonPropertyName("start_date")]
            public string? StartDate { get; set; }

            [JsonPropertyName("end_date")]
            public string? EndDate { get; set; }

            [JsonPropertyName("azmoon_time")]
            public string? ExamTime { get; set; }

            [JsonPropertyName("score_type")]
            public string? ScoreType { get; set; }

            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }
        }

        public sealed class TestDto
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("user_")]
            public string? User { get; set; }

            [JsonPropertyName("question")]
            public string? Question { get; set; }

            [JsonPropertyName("lesson")]
            public string? Lesson { get; set; }

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

            [JsonPropertyName("answer_view_type")]
            public string? AnswerViewType { get; set; }

            [JsonPropertyName("direction")]
            public string? Direction { get; set; }

            [JsonPropertyName("owner")]
            public bool Owner { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("lesson_title")]
            public string? LessonTitle { get; set; }

            [JsonPropertyName("testImgAnswers")]
            public bool TestImageAnswers { get; set; }
        }
    }
}
