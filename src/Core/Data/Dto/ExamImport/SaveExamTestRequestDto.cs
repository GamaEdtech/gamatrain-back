namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A question to create on gama-api, or the new content of question <see cref="Id"/>.</summary>
    public sealed class SaveExamTestRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The signed-in caller here (from the MCP token), which the native source acts for.</summary>
        public long UserId { get; set; }

        /// <summary>The question to change; null creates one.</summary>
        public long? Id { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public int? TopicId { get; set; }

        public int? Level { get; set; }

        /// <summary>fourchoice, twochoice, tf, descriptive, shortanswer or blank.</summary>
        public required string Type { get; set; }

        public required string QuestionHtml { get; set; }

        /// <summary>The option texts A to D; empty for the open types and for image options.</summary>
        public IReadOnlyList<string> OptionsHtml { get; set; } = [];

        /// <summary>The correct option, 1 to 4; null for the open types.</summary>
        public int? CorrectOption { get; set; }

        public string? AnswerHtml { get; set; }

        /// <summary>Where the question comes from.</summary>
        public string? Resource { get; set; }

        /// <summary>The options are images (<see cref="OptionFiles"/>).</summary>
        public bool ImageOptions { get; set; }

        /// <summary>How the options are laid out: 4 in one row, 2 per row, 1 per row (gama-api's <c>answer_view_type</c>).
        /// Null keeps a changed question's layout; a new one then has none, and the Word export picks one from the options' length.</summary>
        public int? ViewType { get; set; }

        /// <summary>gama-api upload keys; one left out keeps the image of a question being changed.</summary>
        public string? QuestionFile { get; set; }

        public IReadOnlyList<string> OptionFiles { get; set; } = [];

        public string? AnswerFile { get; set; }
    }
}
