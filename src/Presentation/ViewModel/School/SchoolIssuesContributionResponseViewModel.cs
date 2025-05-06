namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolIssuesContributionResponseViewModel
    {
        public long Id { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? Status { get; set; }

        public string? Comment { get; set; }

        public string? SchoolName { get; set; }

        public string? Description { get; set; }
    }
}
