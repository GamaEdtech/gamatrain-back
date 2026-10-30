namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>What <c>SyncPlanFeaturesAsync</c> changed. All zero when every active subscriber already had the plan's features.</summary>
    public sealed class SyncPlanFeaturesResponseDto
    {
        /// <summary>Active subscriptions on the plan that gained at least one feature.</summary>
        public int SubscriptionCount { get; set; }

        /// <summary>Features attached in total, across all of those subscriptions.</summary>
        public int FeatureCount { get; set; }

        /// <summary>New quota buckets created (for a feature the plan added outside any group the subscriber already had).</summary>
        public int QuotaCount { get; set; }
    }
}
