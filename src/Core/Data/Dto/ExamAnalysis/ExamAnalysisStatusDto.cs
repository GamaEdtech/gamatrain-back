namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    /// <summary>What the results page needs before the student asks for an analysis: the saved one if any, otherwise
    /// what asking would cost. Reading it never charges.</summary>
    public sealed class ExamAnalysisStatusDto
    {
        /// <summary>The saved analysis, <see langword="null"/> when there is none yet.</summary>
        public ExamAnalysisDto? Analysis { get; set; }

        /// <summary>The points an analysis costs now (the admin-set price); 0 when it is already paid for.</summary>
        public long Points { get; set; }

        /// <summary>Already paid for: the next <c>POST</c> is free, even when no analysis is saved yet (a save that
        /// failed after payment).</summary>
        public bool Purchased { get; set; }
    }
}
