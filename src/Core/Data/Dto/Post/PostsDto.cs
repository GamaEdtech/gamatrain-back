namespace GamaEdtech.Data.Dto.Post
{
    using System;

    using GamaEdtech.Domain.Enumeration;

    public sealed class PostsDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public string? ImageUri { get; set; }
        public VisibilityType VisibilityType { get; set; }
        public DateTimeOffset PublishDate { get; set; }
        public Status Status { get; set; } = Status.Confirmed;
        public string? RejectionComment { get; set; }
        public string? CreationUser { get; set; }
        public DateTimeOffset CreationDate { get; set; }
    }
}
