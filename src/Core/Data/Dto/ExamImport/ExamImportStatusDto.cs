namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Where the caller's import stands (the <c>session_status</c> MCP tool).</summary>
    public sealed class ExamImportStatusDto
    {
        public string? User { get; set; }

        /// <summary>A gama-api admin or sub-admin: can make the exam from a paper already on gamatrain.</summary>
        public bool Staff { get; set; }

        /// <summary>The paper <c>load_paper</c> just started the import from, with a download link to each file.</summary>
        public ExamImportPastPaperDto? Paper { get; set; }

        public ExamImportDetailsDto? Details { get; set; }

        /// <summary>The exam title that will be used.</summary>
        public string? TitleWillBe { get; set; }

        public int Figures { get; set; }

        public ExamImportSummaryDto? Summary { get; set; }

        public ExamImportUploadResultDto? Upload { get; set; }
    }
}
