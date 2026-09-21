namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostEditResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public string? Slug { get; set; }

        public string? Summary { get; set; }

        public string? Body { get; set; }

        public string? ImageUri { get; set; }

        public string? PodcastUri { get; set; }

        public string? Keywords { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }

        public string? RejectionComment { get; set; }

        [JsonConverter(typeof(EnumerationConverter<VisibilityType, byte>))]
        public VisibilityType VisibilityType { get; set; }

        public DateTimeOffset PublishDate { get; set; }

        public IEnumerable<long>? Tags { get; set; }

        public IEnumerable<PostLocalizedValueViewModel>? LocalizedValues { get; set; }
    }
}
