namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class PurchaseSubscriptionResponseViewModel
    {
        public long UserSubscriptionId { get; set; }

        public long PaymentId { get; set; }

        public string? Url { get; set; }
    }
}
