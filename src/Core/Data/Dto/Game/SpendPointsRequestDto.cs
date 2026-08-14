namespace GamaEdtech.Data.Dto.Game
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SpendPointsRequestDto
    {
        public required long UserId { get; set; }
        public required long Points { get; set; }
        public required long IdentifierId { get; set; }
        public required ContentType ContentType { get; set; }

        /// <summary>
        /// How much of the user's subscription quota this action consumes, distinct from
        /// <see cref="Points"/> (the wallet-fallback charge). Defaults to 1 - the pre-existing,
        /// count-based behavior still used by the client-supplied-<see cref="Points"/> `games/spends`
        /// endpoint, where trusting the caller's number for quota too would let it drain a feature's
        /// allowance in one call. Content downloads (<see cref="Data.Dto.Content.DownloadContentRequestDto"/>
        /// via ContentDeliveryService) set this to gama-api's own reported price instead, so quota is
        /// consumed proportionally to what the content actually costs - see
        /// docs/business/content-delivery.md, "Charge: quota-then-points".
        /// </summary>
        public int QuotaAmount { get; set; } = 1;
    }
}
