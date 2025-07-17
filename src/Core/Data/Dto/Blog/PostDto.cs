namespace GamaEdtech.Data.Dto.Blog
{
    using System.Collections.Generic;

    using GamaEdtech.Data.Dto.Tag;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostDto
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public Uri? ImageUri { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public string? CreationUser { get; set; }
        public VisibilityType VisibilityType { get; set; }
        public IEnumerable<TagDto>? Tags { get; set; }
        public DateTimeOffset PublishDate { get; set; }
        public string? Keywords { get; set; }
    }
}
