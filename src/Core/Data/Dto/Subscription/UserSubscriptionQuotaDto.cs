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

        /// <summary>
        /// Already resolved and snapshotted at activation: the pooled bucket's description when this bucket
        /// covers 2+ features, otherwise the single feature's own description. Always one field to render.
        /// </summary>
        public string? Description { get; set; }
    }

    public sealed class UserSubscriptionQuotaFeatureDto
    {
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }

        /// <summary>The parent bucket's already-resolved <see cref="UserSubscriptionQuotaDto.Description"/>, repeated here so this list has the same one-description-per-row shape as an upgrade suggestion's feature list.</summary>
        public string? Description { get; set; }
    }
}
