namespace GamaEdtech.Data.Dto.Game
{
    public sealed class ExamInformationResponseDto
    {
        public ExamDto? Exam { get; set; }
#pragma warning disable CA1002 // Do not expose generic lists
        public List<TestDto>? Tests { get; set; }
#pragma warning restore CA1002 // Do not expose generic lists
        public string? Url { get; set; }

        public sealed class ExamDto
        {
            public string? Type { get; set; }
            public string? ExamType { get; set; }
            public string? Title { get; set; }
            public int TestsCount { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
            public string? ExamTime { get; set; }
            public string? ScoreType { get; set; }
            public string? QrCode { get; set; }
        }

        public sealed class TestDto
        {
            public string? Question { get; set; }
            public string? QuestionFile { get; set; }
            public string? OptionA { get; set; }
            public string? OptionB { get; set; }
            public string? OptionC { get; set; }
            public string? OptionD { get; set; }
            public string? OptionAFile { get; set; }
            public string? OptionBFile { get; set; }
            public string? OptionCFile { get; set; }
            public string? OptionDFile { get; set; }

            /// <summary>Descriptive-type tests (see exam 831) have all four options blank -- skip the MCQ grid for those.</summary>
            public bool HasOptions =>
                !string.IsNullOrWhiteSpace(OptionA) || !string.IsNullOrWhiteSpace(OptionB) ||
                !string.IsNullOrWhiteSpace(OptionC) || !string.IsNullOrWhiteSpace(OptionD);
        }
    }
}
