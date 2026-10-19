namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class CheckoutRecoveryOfferResponseViewModel
    {
        public long SubscriptionPlanId { get; set; }

        public string? PlanTitle { get; set; }

        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        public int DiscountPercent { get; set; }

        /// <summary>The plan's regular price; renewals are charged this.</summary>
        public decimal Price { get; set; }

        /// <summary>The first payment after the discount.</summary>
        public decimal DiscountedPrice { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        public DateTimeOffset ExpirationDate { get; set; }
    }
}
