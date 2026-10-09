namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// Every question of a draft exam as gama-api stores it (text, formulas, figures, options, correct answer, answers):
    /// the data of the in-chat preview widget. The HTML is gama-api's own (only <c>p span b br u</c> tags).
    /// </summary>
    public sealed class ExamImportPreviewDto
    {
        public string? Title { get; set; }

        public IReadOnlyList<DetailDto>? Details { get; set; }

        public IReadOnlyList<QuestionDto>? Questions { get; set; }

        /// <summary>The full page: the draft on gamatrain's exam builder.</summary>
        public Uri? PreviewUrl { get; set; }

        public sealed class DetailDto
        {
            public string? Label { get; set; }

            public string? Value { get; set; }
        }

        public sealed class QuestionDto
        {
            /// <summary>The position on the draft, from 1.</summary>
            public int Number { get; set; }

            public long Id { get; set; }

            public string? Type { get; set; }

            public string? Html { get; set; }

            public Uri? Image { get; set; }

            public IReadOnlyList<OptionDto>? Options { get; set; }

            public string? AnswerHtml { get; set; }

            public Uri? AnswerImage { get; set; }
        }

        public sealed class OptionDto
        {
            public string? Letter { get; set; }

            public string? Html { get; set; }

            public Uri? Image { get; set; }

            public bool Correct { get; set; }
        }
    }
}
