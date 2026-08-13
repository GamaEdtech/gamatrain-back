namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>
    /// One entry per quota bucket a plan grants - one feature, or several sharing a pooled quota (see
    /// <c>SubscriptionPlanFeature.FeatureGroupKey</c>). Mirrors <see cref="SetPlanFeaturesRequestDto"/>'s
    /// write shape (<c>FeatureGroups: [{ FeatureIds, Limits, Description }]</c>) on the read side.
    /// </summary>
    public sealed class PlanFeatureGroupDto
    {
        /// <summary>One entry (unpooled), or several sharing <see cref="Limits"/> as a pooled quota.</summary>
        public required IEnumerable<PlanFeatureDto> Features { get; set; }

        /// <summary>One entry per billing interval this group has a limit defined for - sparse, not every <see cref="Domain.Enumeration.BillingInterval"/> needs an entry.</summary>
        public required IEnumerable<PlanFeatureLimitDto> Limits { get; set; }

        /// <summary>The pooled bucket's description when <see cref="Features"/> has more than one entry, otherwise that single feature's own description.</summary>
        public string? Description { get; set; }
    }
}
