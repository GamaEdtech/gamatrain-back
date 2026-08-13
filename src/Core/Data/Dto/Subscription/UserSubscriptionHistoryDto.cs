namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Self-service view of one of the caller's own past subscriptions (Status Expired/Cancelled) -
    /// GET subscriptions/me only ever returns the current one, this is the history list behind it.
    /// Unlike AdminUserSubscriptionDto, this never carries the owning user's identity or the raw
    /// gateway id - the caller already knows who they are, and ExternalSubscriptionId/Gateway stay
    /// admin-only per docs/business/subscriptions.md.
    /// </summary>
    public sealed class UserSubscriptionHistoryDto
    {
        public long Id { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public UserSubscriptionStatus? Status { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
        public decimal PricePaid { get; set; }
        public Currency? Currency { get; set; }
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>True when this was a gateway-native recurring subscription (ExternalSubscriptionId was set) - false for a one-time/GamaTrain purchase.</summary>
        public bool AutoRenews { get; set; }
    }
}
