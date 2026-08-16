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

        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>Only meaningful when the caller already has an Active subscription (this call gets delegated to a switch internally) - see PurchaseSubscriptionRequestDto.Confirm.</summary>
        [Display]
        public bool Confirm { get; set; }
    }
}
