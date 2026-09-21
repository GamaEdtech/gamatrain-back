namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostsResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public string? Slug { get; set; }

        public string? Summary { get; set; }

        public int LikeCount { get; set; }

        public int DislikeCount { get; set; }

        public string? ImageUri { get; set; }

        [JsonConverter(typeof(EnumerationConverter<VisibilityType, byte>))]
        public VisibilityType? VisibilityType { get; set; }

        public DateTimeOffset PublishDate { get; set; }
    }
}
