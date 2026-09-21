namespace GamaEdtech.Data.Dto.Post
{
    using System.Collections.Generic;

    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    public sealed class ManagePostRequestDto
    {
        public long? Id { get; set; }
        public required long UserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool? Draft { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public VisibilityType? VisibilityType { get; set; }
        public DateTimeOffset? PublishDate { get; set; }
        public IFormFile? Image { get; set; }
        public IFormFile? Podcast { get; set; }
        public bool RemovePodcast { get; set; }
        public IEnumerable<long>? Tags { get; set; }
        public string? Keywords { get; set; }
        public IEnumerable<PostLocalizedValueDto>? LocalizedValues { get; set; }
    }
}
