namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    public sealed class GetDownloadUrlResponseDto
    {
        public required string Url { get; set; }
        public string? Name { get; set; }

        /// <summary>The content owner's id in the source system (e.g. gama-api's CoreId) - resolved to a local ApplicationUser by the caller.</summary>
        public required long OwnerExternalId { get; set; }

        /// <summary>The source's own reported price for this content, in points.</summary>
        public required long Points { get; set; }

        /// <summary>Whether the source considers this specific download already paid for - when true, this backend must not charge the downloader or accrue a commission.</summary>
        public required bool Paid { get; set; }
    }
}
