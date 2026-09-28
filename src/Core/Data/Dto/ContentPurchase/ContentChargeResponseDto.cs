namespace GamaEdtech.Data.Dto.ContentPurchase
{
    using GamaEdtech.Data.Dto.Game;

    /// <summary>
    /// What <c>IContentPurchaseService.ChargeAsync</c> did. Exactly one of: already owned (<see cref="AlreadyPurchased"/>,
    /// nothing charged), free (<see cref="Points"/> 0), charged (<see cref="Spend"/> set -- deliver, then
    /// <c>CompletePurchaseAsync</c>; or <c>RefundAsync</c> if delivery fails), or refused (<see cref="Refused"/> set:
    /// why, plus the current plan and upgrade suggestions, the same as a <c>downloads</c> response).
    /// </summary>
    public sealed class ContentChargeResponseDto
    {
        public long Points { get; set; }

        public bool AlreadyPurchased { get; set; }

        public SpendPointsResponseDto? Spend { get; set; }

        public SpendPointsResponseDto? Refused { get; set; }
    }
}
