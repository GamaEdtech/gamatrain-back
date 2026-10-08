namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A past paper already on gamatrain that matches an import's details (gama's <c>tests</c>).</summary>
    public sealed class ExamImportPastPaperDto
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public int? Year { get; set; }

        public int? Month { get; set; }

        /// <summary>True when an online exam is already linked to it.</summary>
        public bool ExamLinked { get; set; }
    }
}
