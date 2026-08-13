namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageSubscriptionPlanPriceRequestDto
    {
        public long? Id { get; set; }
        public required long SubscriptionPlanId { get; set; }
        public string? CountryCode { get; set; }
        public required Currency Currency { get; set; }
        public required decimal Price { get; set; }
        public required BillingInterval BillingInterval { get; set; }
    }
}
