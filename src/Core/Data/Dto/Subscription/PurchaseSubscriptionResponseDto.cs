namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class PurchaseSubscriptionResponseDto
    {
        public long UserSubscriptionId { get; set; }
        public long PaymentId { get; set; }
        public string? Url { get; set; }
    }
}
