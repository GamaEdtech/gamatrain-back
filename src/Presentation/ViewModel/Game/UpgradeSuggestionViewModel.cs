namespace GamaEdtech.Presentation.ViewModel.Game
{
    using GamaEdtech.Presentation.ViewModel.Subscription;

    public sealed class UpgradeSuggestionViewModel
    {
        public long SubscriptionPlanId { get; set; }

        public string? Title { get; set; }

        /// <summary>The suggested plan's limit for the feature that failed; <see langword="null"/> means unlimited. Plan-wide - identical for every entry in <see cref="Prices"/>.</summary>
        public int? Limit { get; set; }

        public bool Highlight { get; set; }

        /// <summary>One entry per billing interval this plan was suggested at (up to the 3 cheapest per interval, cheapest first).</summary>
        public IEnumerable<UpgradeSuggestionPriceViewModel>? Prices { get; set; }

        /// <summary>The plan's full feature/limit list, not just the one that triggered the suggestion.</summary>
        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }
    }
}
