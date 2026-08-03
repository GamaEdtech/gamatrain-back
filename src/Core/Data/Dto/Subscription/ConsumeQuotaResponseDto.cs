namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// A non-consumed result is not an error: the operation reports why (so callers can fall back
    /// to other charging, e.g. wallet points) and which plans would cover the feature.
    /// </summary>
    public sealed class ConsumeQuotaResponseDto
    {
        public bool Consumed { get; set; }
        public QuotaFailureReason? Reason { get; set; }
        public int? RemainingQuota { get; set; }
        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }

        /// <summary>
        /// The distinct <see cref="BillingInterval"/> names present anywhere in <see cref="UpgradeSuggestions"/>
        /// (e.g. <c>["Monthly", "Yearly"]</c>), in interval order - a ready-made tab/period manifest so the caller
        /// doesn't have to scan every suggested plan's prices to know which periods exist.
        /// </summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
