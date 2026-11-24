namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ActiveSubscriptionPlanPriceViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        public string? CurrencySymbol { get; set; }

        public decimal? Price { get; set; }

        /// <summary>
        /// The first payment at this price during a live sale campaign (<c>campaigns/current</c> says which), when the
        /// caller's purchase would get it (no Active subscription); null otherwise. Renewals are <see cref="Price"/>.
        /// </summary>
        public decimal? CampaignPrice { get; set; }
    }
}
