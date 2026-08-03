namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class ResolvePriceRequestDto
    {
        public required long SubscriptionPlanId { get; set; }
        public required BillingInterval BillingInterval { get; set; }

        /// <summary>Server-derived country; never taken from the client. Ignored while regional pricing is disabled.</summary>
        public string? CountryCode { get; set; }
    }
}
