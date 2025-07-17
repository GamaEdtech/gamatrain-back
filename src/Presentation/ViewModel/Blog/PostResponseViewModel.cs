namespace GamaEdtech.Presentation.ViewModel.Blog
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Tag;

    public sealed class PostResponseViewModel
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public Uri? ImageUri { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public IEnumerable<TagResponseViewModel>? Tags { get; set; }
        public string? CreationUser { get; set; }
        public string? Keywords { get; set; }
        public DateTimeOffset PublishDate { get; set; }

        [JsonConverter(typeof(EnumerationConverter<VisibilityType, byte>))]
        public VisibilityType? VisibilityType { get; set; }
    }
}
