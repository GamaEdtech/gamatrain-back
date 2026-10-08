namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// Every question of an import as it will look (text, formulas, figures, options, correct answer, answers, flags):
    /// the data of the in-chat preview widget and of the full preview page. The HTML fields are built from escaped text
    /// with only <c>p b u br</c> tags, the subset gama-api keeps.
    /// </summary>
    public sealed class ExamImportPreviewDto
    {
        public string? Title { get; set; }

        public IReadOnlyList<DetailDto>? Details { get; set; }

        public ExamImportSummaryDto? Summary { get; set; }

        public IReadOnlyList<QuestionDto>? Questions { get; set; }

        /// <summary>The full preview page (a signed link).</summary>
        public Uri? PreviewUrl { get; set; }

        public sealed class DetailDto
        {
            public string? Label { get; set; }

            public string? Value { get; set; }
        }

        public sealed class QuestionDto
        {
            public string? Number { get; set; }

            public string? Type { get; set; }

            /// <summary>ready, review, blocked or skipped.</summary>
            public string? Status { get; set; }

            public string? StatusLabel { get; set; }

            public string? Html { get; set; }

            public Uri? Image { get; set; }

            public IReadOnlyList<OptionDto>? Options { get; set; }

            public string? AnswerHtml { get; set; }

            public Uri? AnswerImage { get; set; }

            public string? AnswerSource { get; set; }

            public int? Marks { get; set; }

            public string? Topic { get; set; }

            public IReadOnlyList<ExamImportReportDto.IssueDto>? Issues { get; set; }
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
