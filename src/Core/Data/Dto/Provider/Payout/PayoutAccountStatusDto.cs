namespace GamaEdtech.Data.Dto.Provider.Payout
{
    public sealed class PayoutAccountStatusDto
    {
        /// <summary>The owner finished the provider's onboarding form.</summary>
        public bool DetailsSubmitted { get; set; }

        /// <summary>Transfers to the account are allowed and it can pay out to the owner's bank.</summary>
        public bool PayoutsEnabled { get; set; }
    }
}
