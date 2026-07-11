namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SubscriptionPlanResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public IEnumerable<CoordinateViewModel>? Polygon { get; set; }

        public bool IsActive { get; set; }

        public bool Highlight { get; set; }

        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        public IEnumerable<SubscriptionPlanPriceResponseViewModel>? Prices { get; set; }

        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }
    }
}
