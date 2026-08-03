namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class UserSubscriptionQuotaDto
    {
        /// <summary>The feature(s) this bucket covers - more than one when the plan pooled them.</summary>
        public required IEnumerable<UserSubscriptionQuotaFeatureDto> Features { get; set; }

        public int? Limit { get; set; }
        public int Used { get; set; }

        /// <summary><see langword="null"/> means unlimited (<see cref="Limit"/> is <see langword="null"/>).</summary>
        public int? Remaining { get; set; }

        /// <summary>Description of the pooled bucket, snapshotted at activation; <see langword="null"/> for an unpooled bucket - display the single entry in <see cref="Features"/> instead.</summary>
        public string? Description { get; set; }
    }

    public sealed class UserSubscriptionQuotaFeatureDto
    {
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
        public string? FeatureDescription { get; set; }
    }
}
