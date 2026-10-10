namespace GamaEdtech.Data.Dto.ExamImport
{
    using GamaEdtech.Common.Data;

    /// <summary>What a search of gamatrain's paper directory found: the paper whose id was typed, ready to make the exam
    /// from, or a page of the papers whose title has the words.</summary>
    public sealed class ExamImportPaperSearchDto
    {
        /// <summary>The paper whose id was typed, with its exam details and file links (as from <c>load_paper</c>).</summary>
        public ExamImportPastPaperDto? Paper { get; set; }

        /// <summary>Otherwise a page of the papers that have every word, and how many do.</summary>
        public ListDataSource<ExamImportPastPaperDto> Papers { get; set; }
    }
}
