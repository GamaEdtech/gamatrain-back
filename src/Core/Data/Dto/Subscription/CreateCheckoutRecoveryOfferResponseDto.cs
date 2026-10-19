namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class CreateCheckoutRecoveryOfferResponseDto
    {
        /// <summary>The link token for the offer page (Data Protection over the offer id).</summary>
        public required string Token { get; set; }
        public required int DiscountPercent { get; set; }
        public required DateTimeOffset ExpirationDate { get; set; }
    }
}
