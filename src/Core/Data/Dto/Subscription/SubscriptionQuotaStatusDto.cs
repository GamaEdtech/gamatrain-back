namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>
    /// Admin visibility: one of a subscription's current quota buckets, with its live <see cref="Used"/> - unlike
    /// <see cref="UpgradeSuggestionFeatureGroupDto"/> (what a plan *offers*), this is what's actually been
    /// consumed against *this specific* subscription right now. Only ever attached to
    /// <see cref="AdminUserSubscriptionDto.FeatureGroups"/> for the single-subscription admin detail call
    /// (<c>GetUserSubscriptionAsync</c>), not the paged list, since it needs its own query per subscription.
    /// </summary>
    public sealed class SubscriptionQuotaStatusDto
    {
        /// <summary>One entry (unpooled), or several sharing <see cref="Limit"/>/<see cref="Used"/> as a pooled quota.</summary>
        public required IEnumerable<PlanFeatureDto> Features { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        public int Used { get; set; }

        /// <summary><see cref="Limit"/> minus <see cref="Used"/>, floored at 0; <see langword="null"/> when <see cref="Limit"/> is null (unlimited).</summary>
        public int? Remaining { get; set; }

        /// <summary>The pooled bucket's description when <see cref="Features"/> has more than one entry, otherwise that single feature's own description.</summary>
        public string? Description { get; set; }
    }
}
