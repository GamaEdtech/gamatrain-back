namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PurchaseSubscriptionRequestViewModel
    {
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<PaymentGateway, byte>))]
        public PaymentGateway? Gateway { get; set; }
    }
}
