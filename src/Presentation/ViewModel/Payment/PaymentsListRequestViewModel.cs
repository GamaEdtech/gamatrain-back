namespace GamaEdtech.Presentation.ViewModel.Payment
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PaymentsListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public long? UserId { get; set; }

        public long? IdentifierId { get; set; }

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
