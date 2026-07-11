namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class GatewayMappingDto
    {
        public long Id { get; set; }
        public long SubscriptionPlanPriceId { get; set; }
        public PaymentGateway Gateway { get; set; }
        public string? ExternalProductId { get; set; }
        public string? ExternalPlanId { get; set; }
    }
}
