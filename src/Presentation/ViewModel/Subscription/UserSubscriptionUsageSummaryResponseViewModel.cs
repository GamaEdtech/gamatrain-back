namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    /// <summary>The caller's own per-feature usage totals for a date range. Refunds are netted out of both numbers.</summary>
    public sealed class UserSubscriptionUsageSummaryResponseViewModel
    {
        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        /// <summary>Quota used, net of refunds.</summary>
        public int TotalAmount { get; set; }

        /// <summary>Times the feature was used, net of refunded uses.</summary>
        public int UseCount { get; set; }
    }
}
