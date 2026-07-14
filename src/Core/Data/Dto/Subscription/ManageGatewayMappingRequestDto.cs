namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageGatewayMappingRequestDto
    {
        public long? Id { get; set; }
        public required long SubscriptionPlanPriceId { get; set; }
        public required PaymentGateway Gateway { get; set; }
        public required string ExternalProductId { get; set; }
        public string? ExternalPlanId { get; set; }
    }
}
