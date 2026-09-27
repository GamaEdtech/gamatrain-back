namespace GamaEdtech.Data.Dto.Game
{
    public sealed class ExportExamResponseDto
    {
        public byte[]? Content { get; set; }
        public string? FileName { get; set; }

        /// <summary>What this export cost (question count x the format's multiplier); 0 when free (the thumbnail,
        /// or an exam+format this user already bought -- see <see cref="AlreadyPurchased"/>).</summary>
        public long Points { get; set; }

        public bool AlreadyPurchased { get; set; }

        /// <summary>The charge's outcome -- on success who paid (<c>PaidBy</c>); on a refused charge, why, plus the
        /// current plan and upgrade suggestions, the same as a <c>downloads</c> response. <see langword="null"/> when
        /// nothing was charged.</summary>
        public SpendPointsResponseDto? Charge { get; set; }
    }
}
