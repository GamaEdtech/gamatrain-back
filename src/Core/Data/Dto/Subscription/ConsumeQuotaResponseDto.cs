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
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }
    }
}
