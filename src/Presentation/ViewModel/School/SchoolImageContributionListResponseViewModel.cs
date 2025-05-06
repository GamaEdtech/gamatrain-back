namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolImageContributionListResponseViewModel
    {
        public long Id { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public long SchoolId { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }

        public Uri? FileUri { get; set; }

        [JsonConverter(typeof(EnumerationConverter<FileType, byte>))]
        public FileType? FileType { get; set; }
    }
}
