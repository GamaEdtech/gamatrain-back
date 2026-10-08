namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>The <c>submit</c> MCP tool's request: upload the import as a draft exam.</summary>
    public sealed class StartExamImportUploadRequestDto
    {
        public long UserId { get; set; }

        /// <summary>The caller's gama-api token, kept (encrypted) for the background upload.</summary>
        public required string Token { get; set; }

        /// <summary>Also upload the questions flagged for review.</summary>
        public bool IncludeNeedsReview { get; set; }

        /// <summary>What to do with an unpublished draft already on gama-api (only one is allowed): <c>ask</c>,
        /// <c>useExisting</c> (its details and questions are replaced) or <c>deleteExisting</c>.</summary>
        public string ExistingDraft { get; set; } = "ask";
    }
}
