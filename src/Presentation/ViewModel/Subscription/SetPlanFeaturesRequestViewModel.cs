namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SetPlanFeaturesRequestViewModel
    {
        /// <summary>
        /// Replace-all semantics: the plan's feature set becomes exactly this list of groups. A group with
        /// one <see cref="PlanFeatureGroupItemViewModel.FeatureIds"/> entry is a normal, unpooled feature; a
        /// group with two or more means those features share one pooled <see cref="PlanFeatureGroupItemViewModel.Limit"/>.
        /// </summary>
        [Display]
        public IEnumerable<PlanFeatureGroupItemViewModel>? FeatureGroups { get; set; }
    }

    public sealed class PlanFeatureGroupItemViewModel
    {
        /// <summary>One feature id, or several sharing this group's <see cref="Limit"/> as a pooled quota.</summary>
        [Display]
        public IEnumerable<int>? FeatureIds { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        [Display]
        public int? Limit { get; set; }

        /// <summary>Description shown for the pooled bucket; required when <see cref="FeatureIds"/> has more than one entry (a pool has no single feature to describe it), ignored for a single-feature entry.</summary>
        [Display]
        public string? Description { get; set; }
    }
}
