namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class PlanFeatureViewModel
    {
        public int FeatureId { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public string? FeatureDescription { get; set; }

        public int? Limit { get; set; }

        /// <summary>Codes of the other feature(s) this one shares a pooled quota with, if any; <see langword="null"/>/empty when this feature has its own independent quota.</summary>
        public IEnumerable<string>? PooledFeatureCodes { get; set; }

        /// <summary>Description of the pooled bucket (see <see cref="PooledFeatureCodes"/>); <see langword="null"/> when unpooled - fall back to <see cref="FeatureDescription"/> instead.</summary>
        public string? FeatureGroupDescription { get; set; }
    }
}
