namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSubscriptionHistoryResponseViewModel
    {
        public long Id { get; set; }

        public long SubscriptionPlanId { get; set; }

        public string? PlanTitle { get; set; }

        [JsonConverter(typeof(EnumerationConverter<UserSubscriptionStatus, byte>))]
        public UserSubscriptionStatus? Status { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? StartDate { get; set; }

        public DateTimeOffset? ExpirationDate { get; set; }

        public decimal PricePaid { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>True when this was a gateway-native recurring subscription - false for a one-time/GamaTrain purchase.</summary>
        public bool AutoRenews { get; set; }
    }
}
