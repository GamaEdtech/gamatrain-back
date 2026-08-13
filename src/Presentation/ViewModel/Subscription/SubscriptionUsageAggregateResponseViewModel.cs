namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class SubscriptionUsageAggregateResponseViewModel
    {
        public int FeatureId { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public int TotalAmount { get; set; }

        public int EventCount { get; set; }

        public int DistinctUserCount { get; set; }
    }
}
