namespace GamaEdtech.Presentation.ViewModel.ContentReport
{
    /// <summary>A "report a problem": what it is about, the kind of problem picked from the page's list, and a description.</summary>
    public sealed class CreateContentReportRequestViewModel
    {
        /// <summary>Paper, Exam, ExamQuestion, Tutorial, Multimedia or Question (gama-api's names test, azmoon, examTest,
        /// tutorial, learnfile, question work too).</summary>
        public string? ContentType { get; set; }

        public long? ContentId { get; set; }

        /// <summary>The kind of problem (1 to 20, the page's list).</summary>
        public byte? ReportType { get; set; }

        /// <summary>25 to 500 characters.</summary>
        public string? Message { get; set; }
    }

    /// <summary>Staff's decision on a report.</summary>
    public sealed class ContentReportStatusRequestViewModel
    {
        /// <summary>Open, Resolved or Dismissed.</summary>
        public string? Status { get; set; }

        public string? Note { get; set; }
    }
}
