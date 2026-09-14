namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class CommissionStatisticsRequestViewModel
    {
        [Display]
        public DateOnly? StartDate { get; set; }

        [Display]
        public DateOnly? EndDate { get; set; }

        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<Period, byte>))]
        public Period Period { get; set; }
    }
}
