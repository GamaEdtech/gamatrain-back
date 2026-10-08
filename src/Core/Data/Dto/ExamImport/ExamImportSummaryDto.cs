namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Where an import's questions stand: totals for the review step.</summary>
    public sealed class ExamImportSummaryDto
    {
        public int QuestionsFound { get; set; }

        public Dictionary<string, int> ByType { get; set; } = [];

        /// <summary>Question numbers that can be uploaded as they are.</summary>
        public IReadOnlyList<string> Ready { get; set; } = [];

        /// <summary>Question numbers a human should look at; uploaded only when the user agrees.</summary>
        public IReadOnlyList<string> NeedsReview { get; set; } = [];

        /// <summary>Question numbers that cannot be uploaded until fixed or skipped.</summary>
        public IReadOnlyList<string> Blocked { get; set; } = [];

        public IReadOnlyList<string> Skipped { get; set; } = [];

        public IReadOnlyList<string> MissingAnswers { get; set; } = [];

        public IReadOnlyList<string> MissingFigures { get; set; } = [];

        /// <summary>Exam details still needed before uploading.</summary>
        public IReadOnlyList<string> MissingDetails { get; set; } = [];

        public int WillUpload { get; set; }

        public int MinQuestionsToPublish { get; set; }
    }
}
