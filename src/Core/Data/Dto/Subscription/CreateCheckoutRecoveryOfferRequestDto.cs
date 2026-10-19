namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class CreateCheckoutRecoveryOfferRequestDto
    {
        public required long UserId { get; set; }
        public required long AbandonedPaymentId { get; set; }
        public required long SubscriptionPlanId { get; set; }
        public required BillingInterval BillingInterval { get; set; }
    }
}
