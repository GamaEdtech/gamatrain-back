namespace GamaEdtech.Data.Dto.Blog
{
    using System;

    public sealed class PostContributionDto
    {
        public string? Body { get; set; }
        public string? ImageId { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public int CreationUserId { get; set; }
        public IEnumerable<int>? Tags { get; set; }
    }
}
