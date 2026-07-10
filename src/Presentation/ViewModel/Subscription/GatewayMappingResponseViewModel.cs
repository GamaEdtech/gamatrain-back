namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class GatewayMappingResponseViewModel
    {
        public long Id { get; set; }

        public long SubscriptionPlanPriceId { get; set; }

        [JsonConverter(typeof(EnumerationConverter<PaymentGateway, byte>))]
        public PaymentGateway? Gateway { get; set; }

        public string? ExternalProductId { get; set; }

        public string? ExternalPlanId { get; set; }
    }
}
