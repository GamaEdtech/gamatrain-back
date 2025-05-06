namespace GamaEdtech.Presentation.ViewModel.School
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolIssuesContributionReviewResponseViewModel
    {
        public long Id { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? Status { get; set; }

        public long? SchoolId { get; set; }

        public string? SchoolName { get; set; }

        public string? Description { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
