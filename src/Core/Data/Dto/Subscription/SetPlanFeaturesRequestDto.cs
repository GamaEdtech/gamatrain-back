namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class SetPlanFeaturesRequestDto
    {
        public required long SubscriptionPlanId { get; set; }

        /// <summary>
        /// Replace-all semantics: the plan's feature set becomes exactly this list of groups. A group with
        /// one <see cref="PlanFeatureGroupItemDto.FeatureIds"/> entry is a normal, unpooled feature; a group
        /// with two or more means those features share one pooled <see cref="PlanFeatureGroupItemDto.Limit"/>.
        /// </summary>
        public required IEnumerable<PlanFeatureGroupItemDto> FeatureGroups { get; set; }
    }

    public sealed class PlanFeatureGroupItemDto
    {
        /// <summary>One feature id, or several sharing this group's <see cref="Limit"/> as a pooled quota.</summary>
        public required IEnumerable<int> FeatureIds { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        /// <summary>Description shown for the pooled bucket; required when <see cref="FeatureIds"/> has more than one entry (a pool has no single feature to describe it), ignored for a single-feature entry.</summary>
        public string? Description { get; set; }
    }
}
