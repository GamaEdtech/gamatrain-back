namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ActivateUserSubscriptionRequestDto
    {
        public required long UserSubscriptionId { get; set; }

        /// <summary>The gateway's own recurring-subscription id, when the purchase was a Stripe subscription checkout - null otherwise.</summary>
        public string? ExternalSubscriptionId { get; set; }
    }
}
