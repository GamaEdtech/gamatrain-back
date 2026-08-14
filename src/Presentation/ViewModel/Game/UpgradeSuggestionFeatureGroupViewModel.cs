namespace GamaEdtech.Presentation.ViewModel.Game
{
    using GamaEdtech.Presentation.ViewModel.Subscription;

    /// <summary>
    /// One of a suggested plan's quota buckets, already resolved at one specific billing interval (see
    /// <see cref="UpgradeSuggestionPriceViewModel.FeatureGroups"/>) - unlike <see cref="PlanFeatureGroupViewModel"/>,
    /// which carries every interval's limit at once for the admin plan-editing use case, this carries just the
    /// one <see cref="Limit"/> that applies at its containing <see cref="UpgradeSuggestionPriceViewModel.BillingInterval"/>.
    /// </summary>
    public sealed class UpgradeSuggestionFeatureGroupViewModel
    {
        /// <summary>One entry (unpooled), or several sharing <see cref="Limit"/> as a pooled quota.</summary>
        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        /// <summary>The pooled bucket's description when <see cref="Features"/> has more than one entry, otherwise that single feature's own description.</summary>
        public string? Description { get; set; }
    }
}
