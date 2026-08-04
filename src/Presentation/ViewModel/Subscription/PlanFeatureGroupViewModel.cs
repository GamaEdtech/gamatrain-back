namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    /// <summary>
    /// One entry per quota bucket a plan grants - one feature, or several sharing a pooled quota. Mirrors
    /// <see cref="SetPlanFeaturesRequestViewModel"/>'s write shape (<c>featureGroups: [{ featureIds, limit,
    /// description }]</c>) on the read side.
    /// </summary>
    public sealed class PlanFeatureGroupViewModel
    {
        /// <summary>One entry (unpooled), or several sharing <see cref="Limit"/> as a pooled quota.</summary>
        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        /// <summary>The pooled bucket's description when <see cref="Features"/> has more than one entry, otherwise that single feature's own description.</summary>
        public string? Description { get; set; }
    }
}
