namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Where the caller stands (the <c>session_status</c> MCP tool).</summary>
    public sealed class ExamImportStatusDto
    {
        public string? User { get; set; }

        /// <summary>The caller's unpublished draft exam on gama-api (<c>exams/current</c>), if any.</summary>
        public ExamImportDraftDto? Draft { get; set; }
    }
}
