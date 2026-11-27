namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PublicProfilePublishedContentDto
    {
        public int TotalRecordsCount { get; set; }
        public int PastPaperCount { get; set; }
        public int ExamCount { get; set; }
        public int PostCount { get; set; }
        public IReadOnlyList<PublicProfilePastPaperDto> PastPapers { get; set; } = [];
        public IReadOnlyList<PublicProfileExamDto> Exams { get; set; } = [];
        public IReadOnlyList<PublicProfilePostDto> Posts { get; set; } = [];
    }

    public sealed class PublicProfilePastPaperDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? ThumbnailUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public int ViewCount { get; set; }
        public int DownloadCount { get; set; }
        public byte RefereeScore { get; set; }
    }

    public sealed class PublicProfileExamDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? ThumbnailUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public int ViewCount { get; set; }
        public int ParticipantCount { get; set; }
        public int QuestionCount { get; set; }
        public int? DurationMinutes { get; set; }
    }

    public sealed class PublicProfilePostDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? ImageUri { get; set; }
        public DateTimeOffset PublishDate { get; set; }
        public VisibilityType VisibilityType { get; set; }
        public long ViewCount { get; set; }
        public int LikeCount { get; set; }
    }

    public sealed class PublicProfilePublishedContentRequestDto
    {
        public required string ProfileHandle { get; set; }
        public required long UserId { get; set; }
        public int Limit { get; set; }
    }
}
