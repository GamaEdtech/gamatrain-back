namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class GrantUserSubscriptionRequestViewModel
    {
        [Display]
        [Required]
        public long? UserId { get; set; }

        [Display]
        [Required]
        public long? SubscriptionPlanId { get; set; }

        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }
    }
}
