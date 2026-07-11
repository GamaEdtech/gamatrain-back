namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SubscriptionPlanPriceDto
    {
        public long Id { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? CountryCode { get; set; }
        public Currency Currency { get; set; }
        public decimal Price { get; set; }
    }
}
