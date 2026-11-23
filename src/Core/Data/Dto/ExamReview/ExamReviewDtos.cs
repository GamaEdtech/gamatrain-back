namespace GamaEdtech.Data.Dto.ExamReview
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>An exam's reviews for its page: the rating summary of the confirmed ones, the confirmed comments (newest first),
    /// and, for a signed-in caller, their own review and whether they may write one (they finished the exam).</summary>
    public sealed class ExamReviewsDto
    {
        /// <summary>Average of the confirmed ratings, one decimal; null with none.</summary>
        public decimal? Average { get; set; }

        public int Count { get; set; }

        /// <summary>How many confirmed ratings of 1, 2, 3, 4 and 5 (index 0 is 1 star).</summary>
        public IReadOnlyList<int> Distribution { get; set; } = [0, 0, 0, 0, 0];

        /// <summary>Confirmed reviews with a comment, this page.</summary>
        public IReadOnlyList<ExamReviewItemDto> Items { get; set; } = [];

        public int CommentCount { get; set; }

        public ExamReviewItemDto? Mine { get; set; }

        public bool CanReview { get; set; }
    }

    public sealed class ExamReviewItemDto
    {
        public long Id { get; set; }

        /// <summary>The reviewer's first name and last initial ("Ana L."), never their full name or contact.</summary>
        public string? Name { get; set; }

        public string? AvatarUrl { get; set; }

        public byte Rating { get; set; }

        public string? Comment { get; set; }

        public required Status Status { get; set; }

        /// <summary>Why an admin hid it; only on the caller's own review.</summary>
        public string? RejectionComment { get; set; }

        public DateTimeOffset Date { get; set; }

        /// <summary>Until when its author can change it (30 minutes after posting); only on the caller's own review.</summary>
        public DateTimeOffset? EditableUntil { get; set; }
    }

    public sealed class SaveExamReviewRequestDto
    {
        public long UserId { get; set; }

        public long ExamId { get; set; }

        public byte Rating { get; set; }

        public string? Comment { get; set; }
    }

    /// <summary>The admin list of exam reviews, all filters optional.</summary>
    public sealed class ExamReviewAdminListRequestDto
    {
        public Status? Status { get; set; }

        public long? ExamId { get; set; }

        /// <summary>Only those with a comment.</summary>
        public bool? WithComment { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class ExamReviewAdminListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<ExamReviewAdminItemDto> Items { get; set; } = [];
    }

    public sealed class ExamReviewAdminItemDto
    {
        public long Id { get; set; }

        public long ExamId { get; set; }

        public string? ExamTitle { get; set; }

        public long UserId { get; set; }

        /// <summary>The reviewer's name (no email: moderation may be delegated).</summary>
        public string? UserName { get; set; }

        public byte Rating { get; set; }

        public string? Comment { get; set; }

        public required Status Status { get; set; }

        public string? RejectionComment { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? LastModifyDate { get; set; }
    }
}
