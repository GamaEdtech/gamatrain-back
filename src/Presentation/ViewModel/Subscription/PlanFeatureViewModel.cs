namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    /// <summary>Identity only - one entry per feature. <see cref="PlanFeatureGroupViewModel.Limit"/>/description live one level up, since a group can cover more than one feature.</summary>
    public sealed class PlanFeatureViewModel
    {
        public int FeatureId { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }
    }
}
