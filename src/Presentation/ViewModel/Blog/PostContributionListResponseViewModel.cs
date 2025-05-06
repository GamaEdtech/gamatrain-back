namespace GamaEdtech.Presentation.ViewModel.Blog
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostContributionListResponseViewModel
    {
        public long Id { get; set; }

        public string? Comment { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
