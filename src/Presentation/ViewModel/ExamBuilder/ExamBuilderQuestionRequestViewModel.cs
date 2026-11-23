namespace GamaEdtech.Presentation.ViewModel.ExamBuilder
{
    /// <summary>A question: a new one, or a change to one of the caller's. Images are keys from <c>examBuilder/uploads</c>; one
    /// left out keeps the question's image.</summary>
    public sealed class ExamBuilderQuestionRequestViewModel
    {
        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public int? TopicId { get; set; }

        public int? Level { get; set; }

        /// <summary>fourchoice, twochoice, tf, descriptive, blank or shortanswer.</summary>
        public string? Type { get; set; }

        public string? QuestionHtml { get; set; }

        public IReadOnlyList<string> OptionsHtml { get; set; } = [];

        /// <summary>The correct option, 1 to 4.</summary>
        public int? CorrectOption { get; set; }

        public string? AnswerHtml { get; set; }

        public string? Resource { get; set; }

        /// <summary>The options are images (<see cref="OptionFiles"/>).</summary>
        public bool ImageOptions { get; set; }

        public string? QuestionFile { get; set; }

        public IReadOnlyList<string> OptionFiles { get; set; } = [];

        public string? AnswerFile { get; set; }
    }
}
