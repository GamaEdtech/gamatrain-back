namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class SubscriptionPlanResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public IEnumerable<CoordinateViewModel>? Polygon { get; set; }

        public bool IsActive { get; set; }

        public bool Highlight { get; set; }

        public IEnumerable<SubscriptionPlanPriceResponseViewModel>? Prices { get; set; }

        public IEnumerable<PlanFeatureGroupViewModel>? FeatureGroups { get; set; }
    }
}
