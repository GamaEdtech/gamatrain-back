namespace GamaEdtech.Data.Dto.Content
{
    /// <summary>The owner's Stripe Connect payout account, as last read from Stripe.</summary>
    public sealed class PayoutAccountDto
    {
        /// <summary>A Stripe account was created (onboarding started).</summary>
        public bool HasAccount { get; set; }

        public string? Country { get; set; }

        /// <summary>The owner finished Stripe's onboarding form.</summary>
        public bool DetailsSubmitted { get; set; }

        /// <summary>Stripe accepts transfers to the account: a Stripe payout can be requested.</summary>
        public bool PayoutsEnabled { get; set; }
    }
}
