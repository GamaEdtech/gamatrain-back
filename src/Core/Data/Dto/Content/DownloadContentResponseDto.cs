namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadContentResponseDto
    {
        /// <summary>Null when the download didn't happen (e.g. insufficient balance).</summary>
        public string? Url { get; set; }
        public string? Name { get; set; }

        /// <summary>Whether the downloader was charged for this download (false if gama-api already reported it as paid).</summary>
        public bool Spent { get; set; }
        public SpendSource? PaidBy { get; set; }
        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }

        /// <summary>The distinct <see cref="BillingInterval"/> names present anywhere in <see cref="UpgradeSuggestions"/>, in interval order.</summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
