namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class UserSubscriptionQuotaViewModel
    {
        /// <summary>The feature(s) this bucket covers - more than one when the plan pooled them.</summary>
        public IEnumerable<UserSubscriptionQuotaFeatureViewModel>? Features { get; set; }

        public int? Limit { get; set; }

        public int Used { get; set; }

        public int? Remaining { get; set; }

        /// <summary>
        /// Already resolved and snapshotted at activation: the pooled bucket's description when this bucket
        /// covers 2+ features, otherwise the single feature's own description. Always one field to render.
        /// </summary>
        public string? Description { get; set; }
    }

    public sealed class UserSubscriptionQuotaFeatureViewModel
    {
        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        /// <summary>The parent bucket's already-resolved <see cref="UserSubscriptionQuotaViewModel.Description"/>, repeated here so this list has the same one-description-per-row shape as an upgrade suggestion's feature list.</summary>
        public string? Description { get; set; }
    }
}
