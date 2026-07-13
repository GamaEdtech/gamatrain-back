namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadContentRequestViewModel
    {
        [Required]
        public long? Id { get; set; }

        /// <summary>PastPaper, Multimedia, or Exam - any other value is rejected.</summary>
        [JsonConverter(typeof(EnumerationConverter<ContentType, byte>))]
        [Required]
        public ContentType? ContentType { get; set; }

        /// <summary>gama-api's own file-type discriminator: pdf/word/answer/extra. Required for PastPaper only.</summary>
        public string? FileType { get; set; }

        public long? ExtraId { get; set; }
    }
}
