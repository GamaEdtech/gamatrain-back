namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class PlanFeatureViewModel
    {
        public int FeatureId { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public int? Limit { get; set; }

        /// <summary>
        /// Already resolved server-side: the pooled bucket's description when this feature is pooled (see
        /// <see cref="PooledFeatureCodes"/>), otherwise this feature's own description. Always one field to
        /// render, no client-side fallback needed.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>Codes of the other feature(s) this one shares a pooled quota with, if any; <see langword="null"/>/empty when this feature has its own independent quota.</summary>
        public IEnumerable<string>? PooledFeatureCodes { get; set; }
    }
}
