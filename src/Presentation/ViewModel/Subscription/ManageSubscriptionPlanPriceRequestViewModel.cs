namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageSubscriptionPlanPriceRequestViewModel
    {
        [Display]
        public long? SubscriptionPlanId { get; set; }

        /// <summary>Null means the global default price for the plan.</summary>
        [Display]
        public string? CountryCode { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        [Display]
        public decimal? Price { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }
    }
}
