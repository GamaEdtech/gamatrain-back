namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SetPlanFeaturesRequestViewModel
    {
        /// <summary>
        /// Replace-all semantics: the plan's feature set becomes exactly this list of groups. A group with
        /// one <see cref="PlanFeatureGroupItemViewModel.FeatureIds"/> entry is a normal, unpooled feature; a
        /// group with two or more means those features share one pooled quota, with a limit defined per
        /// billing interval via <see cref="PlanFeatureGroupItemViewModel.Limits"/>.
        /// </summary>
        [Display]
        public IEnumerable<PlanFeatureGroupItemViewModel>? FeatureGroups { get; set; }
    }

    public sealed class PlanFeatureGroupItemViewModel
    {
        /// <summary>One feature id, or several sharing this group's <see cref="Limits"/> as a pooled quota.</summary>
        [Display]
        public IEnumerable<int>? FeatureIds { get; set; }

        /// <summary>
        /// One entry per billing interval to define a limit for - sparse, only supply the intervals the plan
        /// is actually sold at. A <see cref="PlanFeatureLimitViewModel.Limit"/> of <see langword="null"/> means
        /// unlimited at that interval. No duplicate <see cref="PlanFeatureLimitViewModel.BillingInterval"/>
        /// within one group.
        /// </summary>
        [Display]
        public IEnumerable<PlanFeatureLimitViewModel>? Limits { get; set; }

        /// <summary>Description shown for the pooled bucket; required when <see cref="FeatureIds"/> has more than one entry (a pool has no single feature to describe it), ignored for a single-feature entry.</summary>
        [Display]
        public string? Description { get; set; }
    }
}
