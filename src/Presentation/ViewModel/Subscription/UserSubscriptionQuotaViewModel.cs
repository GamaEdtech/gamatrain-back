namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class UserSubscriptionQuotaViewModel
    {
        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public int? Limit { get; set; }

        public int Used { get; set; }

        public int? Remaining { get; set; }
    }
}
