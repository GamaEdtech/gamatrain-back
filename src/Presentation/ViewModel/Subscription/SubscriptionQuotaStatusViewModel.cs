namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    /// <summary>
    /// Admin visibility: one of a subscription's current quota buckets, with its live <see cref="Used"/> - unlike
    /// <see cref="GamaEdtech.Presentation.ViewModel.Game.UpgradeSuggestionFeatureGroupViewModel"/> (what a plan
    /// *offers*), this is what's actually been consumed against *this specific* subscription right now. Only
    /// ever populated on the single-subscription admin detail response, never the paged list.
    /// </summary>
    public sealed class SubscriptionQuotaStatusViewModel
    {
        /// <summary>One entry (unpooled), or several sharing <see cref="Limit"/>/<see cref="Used"/> as a pooled quota.</summary>
        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        public int Used { get; set; }

        /// <summary><see cref="Limit"/> minus <see cref="Used"/>, floored at 0; <see langword="null"/> when <see cref="Limit"/> is null (unlimited).</summary>
        public int? Remaining { get; set; }

        /// <summary>The pooled bucket's description when <see cref="Features"/> has more than one entry, otherwise that single feature's own description.</summary>
        public string? Description { get; set; }
    }
}
