namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class ActiveSubscriptionPlanResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public bool Highlight { get; set; }

        /// <summary>One entry per billing interval the plan is offered at (Monthly/Yearly/...).</summary>
        public IEnumerable<ActiveSubscriptionPlanPriceViewModel>? Prices { get; set; }

        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }
    }
}
