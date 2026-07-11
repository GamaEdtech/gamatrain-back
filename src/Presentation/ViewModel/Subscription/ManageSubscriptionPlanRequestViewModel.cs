namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageSubscriptionPlanRequestViewModel
    {
        [Display]
        public string? Title { get; set; }

        [Display]
        public IEnumerable<CoordinateViewModel>? Polygon { get; set; }

        [Display]
        public bool? IsActive { get; set; }

        [Display]
        public bool? Highlight { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }
    }
}
