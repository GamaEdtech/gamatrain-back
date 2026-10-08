namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>The checks of an import's questions: what <c>save_questions</c>, <c>remove_question</c> and <c>review_summary</c>
    /// return.</summary>
    public sealed class ExamImportReportDto
    {
        /// <summary>The question numbers just saved, if any.</summary>
        public IReadOnlyList<string>? Saved { get; set; }

        public string? Removed { get; set; }

        /// <summary>The questions with problems (or every saved one).</summary>
        public IReadOnlyList<QuestionDto>? Issues { get; set; }

        public ExamImportSummaryDto? Summary { get; set; }

        public sealed class QuestionDto
        {
            public string? Number { get; set; }

            /// <summary>ready, review, blocked or skipped.</summary>
            public string? Status { get; set; }

            public IReadOnlyList<IssueDto>? Issues { get; set; }
        }

        public sealed class IssueDto
        {
            /// <summary><c>error</c>: cannot be uploaded until fixed or skipped. <c>review</c>: a human should look.</summary>
            public string? Severity { get; set; }

            public string? Code { get; set; }

            public string? Message { get; set; }
        }
    }
}
