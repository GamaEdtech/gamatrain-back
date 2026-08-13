namespace GamaEdtech.Presentation.ViewModel.Game
{
    public sealed class UpgradeSuggestionViewModel
    {
        /// <summary>The suggested plan's own id - named to match <c>ActiveSubscriptionPlanResponseViewModel.Id</c> (subscriptions/plans) rather than <c>SubscriptionPlanId</c>, so a suggestion entry is schema-compatible with a plan card wherever the frontend needs to render either.</summary>
        public long Id { get; set; }

        public string? Title { get; set; }

        public bool Highlight { get; set; }

        /// <summary>
        /// One entry per billing interval this plan was suggested at (up to the 3 cheapest per interval,
        /// cheapest first) - each interval carries its own <c>limit</c>/<c>featureGroups</c>, since a plan's
        /// quota is no longer identical across Monthly/Yearly/etc.
        /// </summary>
        public IEnumerable<UpgradeSuggestionPriceViewModel>? Prices { get; set; }
    }
}
