namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A question on a draft exam, as gama-api's <c>examTests?exam_id=</c> lists it.</summary>
    public sealed class ExamImportDraftQuestionDto
    {
        public long Id { get; set; }

        /// <summary>fourchoice, twochoice, tf, descriptive, shortanswer or blank.</summary>
        public string? Type { get; set; }

        public string? Html { get; set; }

        public Uri? Image { get; set; }

        /// <summary>The option texts A to D (empty for the open types).</summary>
        public IReadOnlyList<string?> Options { get; set; } = [];

        public IReadOnlyList<Uri?> OptionImages { get; set; } = [];

        /// <summary>The correct option, 1 to 4; 0 for the open types.</summary>
        public int Correct { get; set; }

        public string? AnswerHtml { get; set; }

        public Uri? AnswerImage { get; set; }

        /// <summary>The caller created it.</summary>
        public bool Owner { get; set; }

        /// <summary>Not reviewed into the question bank yet (gama's status 0): the import may still delete it.</summary>
        public bool Pending { get; set; }
    }
}
