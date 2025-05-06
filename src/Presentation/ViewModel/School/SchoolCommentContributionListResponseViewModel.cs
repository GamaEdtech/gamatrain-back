namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.Converter;
    using System.Text.Json.Serialization;

    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolCommentContributionListResponseViewModel
    {
        public long Id { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public long SchoolId { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }
    }
}
