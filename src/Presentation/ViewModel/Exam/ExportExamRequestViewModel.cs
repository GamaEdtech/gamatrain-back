namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ExportExamRequestViewModel
    {
        [Display]
        [Required]
        public long? Id { get; set; }

        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<ExportFileType, byte>))]
        public ExportFileType? FileType { get; set; }

        [Display]
        public string? Watermark { get; set; }

        [Display]
        public int? Duration { get; set; }

        [Display]
        public bool? GoogleDocsCompatible { get; set; }
    }
}
