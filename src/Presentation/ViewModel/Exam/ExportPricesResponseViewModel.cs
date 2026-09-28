namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using System.Collections.Generic;

    /// <summary>An exam's export price per paid format for the current user (<c>GET exams/export/prices</c>).</summary>
    public sealed class ExportPricesResponseViewModel
    {
        public int QuestionCount { get; set; }

        public IEnumerable<ExportPriceViewModel>? Items { get; set; }
    }

    public sealed class ExportPriceViewModel
    {
        /// <summary>The <c>fileType</c> to pass to <c>exams/export</c>: Pdf, Word or PowerPoint.</summary>
        public string? FileType { get; set; }

        /// <summary>Question count x the format's multiplier, rounded up -- charged from ExamDownload quota, then points.</summary>
        public long Points { get; set; }

        /// <summary>Already bought: exporting this format again is free.</summary>
        public bool Purchased { get; set; }
    }
}
