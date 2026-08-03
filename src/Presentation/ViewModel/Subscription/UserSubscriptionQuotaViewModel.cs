namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class UserSubscriptionQuotaViewModel
    {
        /// <summary>The feature(s) this bucket covers - more than one when the plan pooled them.</summary>
        public IEnumerable<UserSubscriptionQuotaFeatureViewModel>? Features { get; set; }

        public int? Limit { get; set; }

        public int Used { get; set; }

        public int? Remaining { get; set; }

        /// <summary>Description of the pooled bucket, snapshotted at activation; <see langword="null"/> for an unpooled bucket - display the single entry in <see cref="Features"/> instead.</summary>
        public string? Description { get; set; }
    }

    public sealed class UserSubscriptionQuotaFeatureViewModel
    {
        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public string? FeatureDescription { get; set; }
    }
}
