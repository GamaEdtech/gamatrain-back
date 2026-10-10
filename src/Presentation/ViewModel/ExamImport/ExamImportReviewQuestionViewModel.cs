namespace GamaEdtech.Presentation.ViewModel.ExamImport
{
    using System.ComponentModel;

    /// <summary>
    /// A question that still needs the user's decision in the review: the <c>open_review</c> MCP tool's public input, a
    /// <c>save_questions</c> result row passed back as it came. The descriptions are what the AI reads in the tool's schema.
    /// </summary>
    public sealed class ExamImportReviewQuestionViewModel
    {
        [Description("The question's label, e.g. 3(b)(ii).")]
        public required string Number { get; set; }

        [Description("Its id on Gamatrain when it was saved.")]
        public long? Id { get; set; }

        [Description("review (saved, a human should look), blocked or failed (not saved).")]
        public required string Status { get; set; }

        [Description("Its issues, as save_questions gave them.")]
        public IReadOnlyList<IssueViewModel>? Issues { get; set; }

        [Description("Gamatrain's error, for a failed question.")]
        public string? Error { get; set; }

        public sealed class IssueViewModel
        {
            public string? Severity { get; set; }

            public string? Code { get; set; }

            public string? Message { get; set; }
        }
    }
}
