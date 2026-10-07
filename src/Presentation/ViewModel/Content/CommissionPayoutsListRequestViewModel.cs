namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public class CommissionPayoutsListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        /// <summary>Optional - omit for every status.</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<PayoutStatus, byte>))]
        public PayoutStatus? Status { get; set; }
    }
}
