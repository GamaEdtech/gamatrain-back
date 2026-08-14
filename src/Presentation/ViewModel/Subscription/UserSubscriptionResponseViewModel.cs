namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSubscriptionResponseViewModel
    {
        public long Id { get; set; }

        public long SubscriptionPlanId { get; set; }

        public string? PlanTitle { get; set; }

        [JsonConverter(typeof(EnumerationConverter<UserSubscriptionStatus, byte>))]
        public UserSubscriptionStatus? Status { get; set; }

        public DateTimeOffset? StartDate { get; set; }

        public DateTimeOffset? ExpirationDate { get; set; }

        public decimal PricePaid { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>True for a gateway-native recurring subscription (Stripe today) - false for a one-time/GamaTrain purchase, which never auto-renews.</summary>
        public bool AutoRenews { get; set; }

        /// <summary>True once the user has requested cancellation - still Active/usable until ExpirationDate, then stops.</summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>Set only when a downgrade (POST subscriptions/me/switch to a cheaper plan) is pending - null otherwise. Takes effect at ExpirationDate above, no separate date field.</summary>
        public long? PendingSwitchPlanId { get; set; }

        /// <summary>Paired with PendingSwitchPlanId - null whenever that is.</summary>
        public string? PendingSwitchPlanTitle { get; set; }

        /// <summary>Set while the gateway's own dunning/Smart Retries are ongoing after a failed renewal charge - null otherwise (including once a later retry succeeds). Visibility only - still fully usable until ExpirationDate.</summary>
        public DateTimeOffset? LastPaymentFailedDate { get; set; }

        public IEnumerable<UserSubscriptionQuotaViewModel>? FeatureGroups { get; set; }
    }
}
