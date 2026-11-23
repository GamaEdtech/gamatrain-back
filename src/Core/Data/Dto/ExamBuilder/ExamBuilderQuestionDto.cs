namespace GamaEdtech.Data.Dto.ExamBuilder
{
    /// <summary>
    /// One of the caller's own questions with every field a change takes (<c>PUT examBuilder/questions/{id}</c>), so a client
    /// can send it back unchanged but for what it means to change. Images come as their stored keys (what a change takes)
    /// and as links.
    /// </summary>
    public sealed class ExamBuilderQuestionDto
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        /// <summary>gama-api's question status: 0 waiting, 1 published, 2 the batch account's, ...</summary>
        public byte Status { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        /// <summary>The question's topic (the last one when it has several).</summary>
        public int? TopicId { get; set; }

        public int? Level { get; set; }

        /// <summary>fourchoice, twochoice, tf, descriptive, blank or shortanswer.</summary>
        public string? Type { get; set; }

        public string? QuestionHtml { get; set; }

        /// <summary>The option texts A to D.</summary>
        public IReadOnlyList<string?> OptionsHtml { get; set; } = [];

        public int? CorrectOption { get; set; }

        public string? AnswerHtml { get; set; }

        public string? Resource { get; set; }

        /// <summary>The options are images (<see cref="OptionFiles"/>).</summary>
        public bool ImageOptions { get; set; }

        /// <summary>How the options are laid out: 4 in one row, 2 per row, 1 per row (gama-api's <c>answer_view_type</c>); worked
        /// out from the question on every save, so a change doesn't take it.</summary>
        public int? ViewType { get; set; }

        public string? QuestionFile { get; set; }

        public IReadOnlyList<string?> OptionFiles { get; set; } = [];

        public string? AnswerFile { get; set; }

        public string? QuestionImage { get; set; }

        public IReadOnlyList<string?> OptionImages { get; set; } = [];

        public string? AnswerImage { get; set; }
    }
}
