namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class UpgradeSuggestionDto
    {
        public long SubscriptionPlanId { get; set; }
        public string? Title { get; set; }

        /// <summary>The suggested plan's limit for the feature that failed; <see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        public bool Highlight { get; set; }
        public Currency? Currency { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal? Price { get; set; }

        /// <summary>The plan's full feature/limit list, not just the one that triggered the suggestion.</summary>
        public IEnumerable<PlanFeatureDto>? Features { get; set; }
    }
}
