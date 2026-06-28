namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ActiveSubscriptionPlanResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency Currency { get; set; }

        public string CurrencySymbol { get; set; }

        public decimal Price { get; set; }

        public long Point { get; set; }

        public bool Highlight { get; set; }

        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval BillingInterval { get; set; }
    }
}
