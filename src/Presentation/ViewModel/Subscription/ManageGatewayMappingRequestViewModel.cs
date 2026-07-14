namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageGatewayMappingRequestViewModel
    {
        [Display]
        public long? SubscriptionPlanPriceId { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<PaymentGateway, byte>))]
        public PaymentGateway? Gateway { get; set; }

        [Display]
        public string? ExternalProductId { get; set; }

        [Display]
        public string? ExternalPlanId { get; set; }
    }
}
