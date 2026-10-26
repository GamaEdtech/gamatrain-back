namespace GamaEdtech.Data.Dto.Game
{
    /// <summary>A student's own exam result in full, from gama-api's <c>exams/result/{id}</c> (see
    /// <c>ICoreProvider.GetExamResultDetailAsync</c>).</summary>
    public sealed class ExamResultDetailResponseDto
    {
        public required ExamResultResponseDto Stats { get; set; }
        public string? ExamTitle { get; set; }

        /// <summary>The time allowed, in minutes, as gama-api sends it.</summary>
        public string? ExamTime { get; set; }

        public int? TimeTakenSeconds { get; set; }
        public IReadOnlyList<RankDto> Ranks { get; set; } = [];
        public IReadOnlyList<QuestionDto> Questions { get; set; } = [];

        /// <param name="Scope">gama-api's rank scope: total, state or area.</param>
        /// <param name="Position">The student's place.</param>
        /// <param name="Participants">How many took part in that scope.</param>
        public sealed record RankDto(string Scope, int Position, int? Participants);

        public sealed class QuestionDto
        {
            public string? Question { get; set; }
            public string? QuestionFile { get; set; }

            /// <summary>gama-api's "fourchoice" or "descriptive".</summary>
            public string? Type { get; set; }

            /// <summary>Options A-D in order; text and image may both be empty.</summary>
            public IReadOnlyList<OptionDto> Options { get; set; } = [];

            public char? CorrectOption { get; set; }

            /// <summary>The option the student chose, <see langword="null"/> when unanswered.</summary>
            public char? StudentOption { get; set; }

            public string? Lesson { get; set; }
            public string? Topics { get; set; }

            public bool IsDescriptive => Type == "descriptive" ||
                (Type != "fourchoice" && Options.All(t => string.IsNullOrWhiteSpace(t.Text) && string.IsNullOrWhiteSpace(t.File)));
        }

        public sealed record OptionDto(string? Text, string? File);
    }
}
