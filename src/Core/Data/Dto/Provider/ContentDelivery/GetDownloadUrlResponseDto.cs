namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    public sealed class GetDownloadUrlResponseDto
    {
        public required string Url { get; set; }
        public string? Name { get; set; }

        /// <summary>The content owner's id in the source system (e.g. gama-api's CoreId), when the source reports one - only /tests/download does. Null means no commission can be accrued.</summary>
        public long? OwnerExternalId { get; set; }

        /// <summary>The source's own reported price for this content, in points, when the source reports one - only /tests/download does. Null means no charge applies at all.</summary>
        public long? Points { get; set; }

        /// <summary>Whether the source considers this specific download already paid for - null when the source doesn't report pricing at all (Multimedia/Exam), true/false only for priced content (PastPaper/Test).</summary>
        public bool? Paid { get; set; }
    }
}
