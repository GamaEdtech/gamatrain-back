namespace GamaEdtech.Data.Dto.ExamAdmin
{
    /// <summary>The admin list of exam attempts, all filters optional. <c>Status</c>: 0 started, 1 finished.</summary>
    public sealed class ExamParticipationListRequestDto
    {
        public long? ExamId { get; set; }

        public long? UserId { get; set; }

        public byte? Status { get; set; }

        /// <summary>Attempts started on or after this instant.</summary>
        public DateTimeOffset? From { get; set; }

        /// <summary>Attempts started before this instant.</summary>
        public DateTimeOffset? ToExclusive { get; set; }

        /// <summary>The participant's name, user name or email contains it.</summary>
        public string? Text { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class ExamParticipationListDto
    {
        public int Total { get; set; }

        /// <summary>Distinct participants among all the matching attempts (not only this page).</summary>
        public int Participants { get; set; }

        /// <summary>Finished attempts among all the matching attempts.</summary>
        public int Finished { get; set; }

        public IReadOnlyList<ExamParticipationItemDto> Items { get; set; } = [];
    }

    public sealed class ExamParticipationItemDto
    {
        public long Id { get; set; }

        public long ExamId { get; set; }

        public string? ExamTitle { get; set; }

        /// <summary>The participant here; null for an attempt copied from gama-api whose user isn't linked yet.</summary>
        public long? UserId { get; set; }

        /// <summary>The participant's gama-api id (0 for an attempt made here by a user without one).</summary>
        public long CoreUserId { get; set; }

        public string? UserName { get; set; }

        public string? Email { get; set; }

        /// <summary>0 started, 1 finished.</summary>
        public byte Status { get; set; }

        /// <summary>The result as gama-api keeps it (a percentage), null until finished.</summary>
        public string? ResultScore { get; set; }

        /// <summary>Seconds from start to submit, when known.</summary>
        public int? SubmitSeconds { get; set; }

        public DateTimeOffset StartDate { get; set; }
    }
}
