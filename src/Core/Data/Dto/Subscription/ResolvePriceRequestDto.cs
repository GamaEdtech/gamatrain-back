namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ResolvePriceRequestDto
    {
        public required long SubscriptionPlanId { get; set; }

        /// <summary>Server-derived country; never taken from the client. Ignored while regional pricing is disabled.</summary>
        public string? CountryCode { get; set; }
    }
}
