namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SwitchSubscriptionPlanRequestViewModel
    {
        [Display]
        [Required]
        public long? SubscriptionPlanId { get; set; }

        /// <summary>Optional - omitted keeps the current interval. A bigger interval applies immediately; a smaller one defers to the current period's end. See SwitchSubscriptionPlanRequestDto.</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        /// <summary>Defaults false. Set true only on a resubmit after the caller has shown the user the previewed charge from a prior false-Confirm response. See SwitchSubscriptionPlanRequestDto.Confirm.</summary>
        [Display]
        public bool Confirm { get; set; }
    }
}
