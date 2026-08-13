namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class SetPlanFeaturesRequestDto
    {
        public required long SubscriptionPlanId { get; set; }

        /// <summary>
        /// Replace-all semantics: the plan's feature set becomes exactly this list of groups. A group with
        /// one <see cref="PlanFeatureGroupItemDto.FeatureIds"/> entry is a normal, unpooled feature; a group
        /// with two or more means those features share one pooled quota, with a limit defined per billing
        /// interval via <see cref="PlanFeatureGroupItemDto.Limits"/>.
        /// </summary>
        public required IEnumerable<PlanFeatureGroupItemDto> FeatureGroups { get; set; }
    }

    public sealed class PlanFeatureGroupItemDto
    {
        /// <summary>One feature id, or several sharing this group's <see cref="Limits"/> as a pooled quota.</summary>
        public required IEnumerable<int> FeatureIds { get; set; }

        /// <summary>
        /// One entry per billing interval to define a limit for - sparse, admins only need to supply the
        /// intervals the plan is actually sold at. A <see cref="PlanFeatureLimitDto.Limit"/> of
        /// <see langword="null"/> means unlimited at that interval. No duplicate
        /// <see cref="PlanFeatureLimitDto.BillingInterval"/> within one group.
        /// </summary>
        public required IEnumerable<PlanFeatureLimitDto> Limits { get; set; }

        /// <summary>Description shown for the pooled bucket; required when <see cref="FeatureIds"/> has more than one entry (a pool has no single feature to describe it), ignored for a single-feature entry.</summary>
        public string? Description { get; set; }
    }
}
