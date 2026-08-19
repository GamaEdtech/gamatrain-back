namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Admin-facing view of a user's subscription - unlike the caller-scoped UserSubscriptionDto, this
    /// carries the owning user's identity and the raw gateway id, since an admin support case needs both.
    /// </summary>
    public sealed class AdminUserSubscriptionDto
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string? UserEmail { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public UserSubscriptionStatus? Status { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
        public decimal PricePaid { get; set; }
        public Currency? Currency { get; set; }
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>True when this is a gateway-native recurring subscription (ExternalSubscriptionId is set).</summary>
        public bool AutoRenews { get; set; }
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>Set only when a downgrade is pending - null otherwise. Takes effect at ExpirationDate above.</summary>
        public long? PendingSwitchPlanId { get; set; }
        public string? PendingSwitchPlanTitle { get; set; }

        /// <summary>Paired with PendingSwitchPlanId - null whenever that is. The interval the pending switch takes effect at (e.g. a Yearly -&gt; Monthly downgrade).</summary>
        public BillingInterval? PendingSwitchBillingInterval { get; set; }

        /// <summary>Set while the gateway's own dunning/Smart Retries are ongoing after a failed renewal charge - null otherwise. Visibility only - Status/ExpirationDate/quota are unaffected.</summary>
        public DateTimeOffset? LastPaymentFailedDate { get; set; }

        public string? ExternalSubscriptionId { get; set; }
        public PaymentGateway? Gateway { get; set; }

        /// <summary>
        /// Current quota status (live Used/Limit per feature group) - only populated by the single-subscription
        /// detail call (<c>GetUserSubscriptionAsync</c>), always <see langword="null"/> on the paged list
        /// (<c>GetUserSubscriptionsAsync</c>), since it needs its own query per subscription.
        /// </summary>
        public IEnumerable<SubscriptionQuotaStatusDto>? FeatureGroups { get; set; }
    }
}
