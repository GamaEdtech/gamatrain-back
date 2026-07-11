namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PurchaseSubscriptionRequestDto
    {
        public required long UserId { get; set; }
        public required long SubscriptionPlanId { get; set; }
        public required PaymentGateway Gateway { get; set; }
    }
}
