namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManagedPostsResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public string? Slug { get; set; }

        public string? ImageUri { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }

        public string? RejectionComment { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset PublishDate { get; set; }

        [JsonConverter(typeof(EnumerationConverter<VisibilityType, byte>))]
        public VisibilityType VisibilityType { get; set; }
    }
}
