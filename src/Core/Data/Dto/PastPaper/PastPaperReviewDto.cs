namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>An admin's review of a past paper.</summary>
    public sealed class PastPaperReviewDto
    {
        public long Id { get; set; }

        /// <summary>The new <c>PastPaperStatus</c> value: 0, 1, 2, 3 or 4.</summary>
        public byte Status { get; set; }

        /// <summary>What the teacher should change, one note per item.</summary>
        public IReadOnlyList<string>? Notes { get; set; }
    }
}
