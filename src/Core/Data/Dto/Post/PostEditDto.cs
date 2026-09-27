namespace GamaEdtech.Data.Dto.Post
{
    using System;
    using System.Collections.Generic;

    using GamaEdtech.Domain.Enumeration;

    public sealed class PostEditDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public string? ImageUri { get; set; }
        public string? PodcastUri { get; set; }
        public string? Keywords { get; set; }
        public VisibilityType VisibilityType { get; set; }
        public DateTimeOffset PublishDate { get; set; }
        public Status Status { get; set; } = Status.Draft;
        public string? RejectionComment { get; set; }
        public IEnumerable<long>? Tags { get; set; }
        public IEnumerable<PostLocalizedValueDto>? LocalizedValues { get; set; }
    }
}
