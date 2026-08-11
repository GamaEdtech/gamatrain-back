namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    public sealed class VerifyResponseDto
    {
        public string? Mint { get; set; }
        public string? SourceWallet { get; set; }

        /// <summary>The gateway's own recurring-subscription id, set only for a subscription-mode purchase on a gateway that supports recurring billing (Stripe) - <see langword="null"/> for every other verify call.</summary>
        public string? ExternalSubscriptionId { get; set; }
    }
}
