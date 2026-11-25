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

        [JsonConverter(typeof(EnumerationConverter<DownloadContentType, byte>))]
        [Required]
        public DownloadContentType? ContentType { get; set; }

        /// <summary>The past paper file to download: its id in the paper's <c>files</c> list. Required for PastPaper only
        /// (since 2026-11-25; before, gama-api's <c>fileType</c>/<c>extraId</c>).</summary>
        public long? FileId { get; set; }
    }
}
