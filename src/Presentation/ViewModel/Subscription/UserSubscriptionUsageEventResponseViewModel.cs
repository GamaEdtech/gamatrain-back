namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    /// <summary>One row of the caller's own quota usage log. A negative Amount is a refund (the download failed after the charge).</summary>
    public sealed class UserSubscriptionUsageEventResponseViewModel
    {
        public long Id { get; set; }

        public long UserSubscriptionId { get; set; }

        public string? PlanTitle { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public int Amount { get; set; }

        /// <summary>The content item the quota was used for (a paper, multimedia, test or exam id, by FeatureCode); null when not recorded.</summary>
        public long? IdentifierId { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
