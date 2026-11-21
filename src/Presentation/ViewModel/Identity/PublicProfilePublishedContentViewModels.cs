namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PublicProfilePublishedContentRequestViewModel
    {
        [Display]
        [Range(1, 20)]
        public int Limit { get; set; } = 6;
    }

    public sealed class PublicProfilePublishedContentResponseViewModel
    {
        public int TotalRecordsCount { get; set; }
        public int PastPaperCount { get; set; }
        public int ExamCount { get; set; }
        public int PostCount { get; set; }
        public IReadOnlyList<PublicProfilePastPaperViewModel> PastPapers { get; set; } = [];
        public IReadOnlyList<PublicProfileExamViewModel> Exams { get; set; } = [];
        public IReadOnlyList<PublicProfilePostViewModel> Posts { get; set; } = [];
    }

    public sealed class PublicProfilePastPaperViewModel
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public string? ThumbnailUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public int ViewCount { get; set; }
        public int DownloadCount { get; set; }
        public byte RefereeScore { get; set; }
    }

    public sealed class PublicProfileExamViewModel
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

    public sealed class PublicProfilePostViewModel
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? ImageUri { get; set; }
        public DateTimeOffset PublishDate { get; set; }
        public long ViewCount { get; set; }
        public int LikeCount { get; set; }
    }
}
