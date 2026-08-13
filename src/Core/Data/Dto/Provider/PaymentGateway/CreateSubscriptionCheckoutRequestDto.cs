namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    public sealed class CreateSubscriptionCheckoutRequestDto
    {
        public required long PaymentId { get; set; }
        public required long UserSubscriptionId { get; set; }

        /// <summary>The gateway's own recurring price/plan id, already resolved from <see cref="Domain.Entity.SubscriptionPlanGatewayMapping.ExternalPlanId"/> - the provider never looks this up itself.</summary>
        public required string ExternalPriceId { get; set; }
        public required string? CallbackUrl { get; set; }
        public string? Email { get; set; }
    }
}
