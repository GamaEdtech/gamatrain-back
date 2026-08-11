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

        public IEnumerable<UserSubscriptionQuotaViewModel>? FeatureGroups { get; set; }
    }
}
