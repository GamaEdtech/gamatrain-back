namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Where the caller's import stands (the <c>session_status</c> MCP tool).</summary>
    public sealed class ExamImportStatusDto
    {
        public string? User { get; set; }

        public ExamImportDetailsDto? Details { get; set; }

        /// <summary>The exam title that will be used.</summary>
        public string? TitleWillBe { get; set; }

        public int Figures { get; set; }

        public ExamImportSummaryDto? Summary { get; set; }

        public ExamImportUploadResultDto? Upload { get; set; }
    }
}
