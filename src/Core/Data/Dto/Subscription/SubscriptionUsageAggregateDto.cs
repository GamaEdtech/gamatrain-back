namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>
    /// Admin visibility: one feature's totals within a requested date range - either scoped to one user
    /// (GetUsageAggregateRequestDto.UserId set) or across every user (unset), for a usage dashboard.
    /// </summary>
    public sealed class SubscriptionUsageAggregateDto
    {
        public int FeatureId { get; set; }
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
        public int TotalAmount { get; set; }
        public int EventCount { get; set; }

        /// <summary>Distinct users who consumed this feature at least once in the range - always 1 when the request was scoped to a single UserId.</summary>
        public int DistinctUserCount { get; set; }
    }
}
