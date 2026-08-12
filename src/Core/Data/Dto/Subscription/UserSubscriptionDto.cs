namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSubscriptionDto
    {
        public long Id { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public UserSubscriptionStatus? Status { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
        public decimal PricePaid { get; set; }
        public Currency? Currency { get; set; }
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>True when this subscription is a gateway-native recurring subscription (ExternalSubscriptionId is set) - false for a one-time/GamaTrain purchase, which never auto-renews.</summary>
        public bool AutoRenews { get; set; }

        /// <summary>True once the user has requested cancellation - still Active/usable until ExpirationDate, then stops.</summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>Set only when a downgrade (POST subscriptions/me/switch to a cheaper plan) is pending - null otherwise. The switch takes effect at this same subscription's ExpirationDate, already exposed above - no separate date field needed.</summary>
        public long? PendingSwitchPlanId { get; set; }

        /// <summary>Paired with PendingSwitchPlanId - null whenever that is.</summary>
        public string? PendingSwitchPlanTitle { get; set; }

        public IEnumerable<UserSubscriptionQuotaDto>? FeatureGroups { get; set; }
    }
}
