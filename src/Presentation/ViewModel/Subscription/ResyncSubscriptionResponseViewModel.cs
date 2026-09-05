namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class ResyncSubscriptionResponseViewModel
    {
        public bool Synced { get; set; }
        public DateTimeOffset? NewExpirationDate { get; set; }
        public string? GatewayStatus { get; set; }
    }
}
