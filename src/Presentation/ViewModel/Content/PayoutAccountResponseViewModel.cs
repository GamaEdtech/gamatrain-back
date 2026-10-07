namespace GamaEdtech.Presentation.ViewModel.Content
{
    public sealed class PayoutAccountResponseViewModel
    {
        /// <summary>Stripe setup was started.</summary>
        public bool HasAccount { get; set; }

        public string? Country { get; set; }

        /// <summary>The Stripe onboarding form was finished.</summary>
        public bool DetailsSubmitted { get; set; }

        /// <summary>A Stripe payout can be requested.</summary>
        public bool PayoutsEnabled { get; set; }
    }
}
