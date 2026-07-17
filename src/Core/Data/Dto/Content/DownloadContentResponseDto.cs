namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadContentResponseDto
    {
        public required string Url { get; set; }
        public string? Name { get; set; }

        /// <summary>Whether the downloader was charged for this download (false if gama-api already reported it as paid).</summary>
        public bool Spent { get; set; }
        public SpendSource? PaidBy { get; set; }
    }
}
