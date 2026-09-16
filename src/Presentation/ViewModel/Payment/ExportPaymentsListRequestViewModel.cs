namespace GamaEdtech.Presentation.ViewModel.Payment
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ExportPaymentsListRequestViewModel
    {
        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<PaymentGateway, byte>))]
        public PaymentGateway? Gateway { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<PaymentStatus, byte>))]
        public PaymentStatus? Status { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<PaymentKind, byte>))]
        public PaymentKind? Kind { get; set; }
    }
}
