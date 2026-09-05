namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ResyncSubscriptionResponseDto
    {
        /// <summary>True when the gateway confirmed this subscription is still active and ExpirationDate/quota were synced to it. False whenever nothing was changed - see <see cref="GatewayStatus"/> for why.</summary>
        public required bool Synced { get; set; }

        /// <summary>The new ExpirationDate when <see cref="Synced"/> - null otherwise.</summary>
        public DateTimeOffset? NewExpirationDate { get; set; }

        /// <summary>The gateway's own raw status string (e.g. "active", "past_due", "canceled") - populated whether or not <see cref="Synced"/>, so an admin can see why a resync did or didn't apply without needing to check the gateway dashboard separately.</summary>
        public string? GatewayStatus { get; set; }
    }
}
