namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class SubscriptionPlansResponseViewModel
    {
        public IEnumerable<ActiveSubscriptionPlanResponseViewModel>? Plans { get; set; }

        /// <summary>The distinct billing-interval names present anywhere in <see cref="Plans"/>' prices, in interval order.</summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
