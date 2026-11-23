namespace GamaEdtech.Presentation.ViewModel.ExamReview
{
    /// <summary>The caller's review of an exam: a rating of 1 to 5 and an optional comment (plain text, at most 1000 characters).</summary>
    public sealed class SaveExamReviewRequestViewModel
    {
        public byte? Rating { get; set; }

        public string? Comment { get; set; }
    }

    /// <summary>An exam's reviews for its page.</summary>
    public sealed class ExamReviewsResponseViewModel
    {
        /// <summary>Average of the published ratings, one decimal; null with none.</summary>
        public decimal? Average { get; set; }

        public int Count { get; set; }

        /// <summary>How many published ratings of 1, 2, 3, 4 and 5 stars.</summary>
        public IReadOnlyList<int> Distribution { get; set; } = [];

        public IReadOnlyList<ExamReviewViewModel> Items { get; set; } = [];

        /// <summary>Published reviews with a comment, all pages.</summary>
        public int CommentCount { get; set; }

        /// <summary>The signed-in caller's own review (any status).</summary>
        public ExamReviewViewModel? Mine { get; set; }

        /// <summary>The signed-in caller finished the exam and may review it.</summary>
        public bool CanReview { get; set; }
    }

    public sealed class ExamReviewViewModel
    {
        public long Id { get; set; }

        /// <summary>"Ana L.": first name and last initial.</summary>
        public string? Name { get; set; }

        public string? AvatarUrl { get; set; }

        public byte Rating { get; set; }

        public string? Comment { get; set; }

        /// <summary>Confirmed (shown) or Rejected (hidden by an admin).</summary>
        public string? Status { get; set; }

        public string? RejectionComment { get; set; }

        public DateTimeOffset Date { get; set; }

        /// <summary>Until when the caller can change their own review (30 minutes after posting).</summary>
        public DateTimeOffset? EditableUntil { get; set; }
    }

    /// <summary>An admin's decision on a review.</summary>
    public sealed class ExamReviewModerationRequestViewModel
    {
        /// <summary>True shows the review, false hides it.</summary>
        public bool? Confirm { get; set; }

        /// <summary>Why it was hidden, for its author (at most 300 characters).</summary>
        public string? RejectionComment { get; set; }
    }
}
