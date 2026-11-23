namespace GamaEdtech.Data.Dto.ContentReport
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class CreateContentReportRequestDto
    {
        public long UserId { get; set; }

        public required ReportedContentType ContentType { get; set; }

        public long ContentId { get; set; }

        public byte ReportType { get; set; }

        public string? Message { get; set; }
    }

    /// <summary>The admin list of reports, all filters optional.</summary>
    public sealed class ContentReportListRequestDto
    {
        public ContentReportStatus? Status { get; set; }

        public ReportedContentType? ContentType { get; set; }

        public long? ContentId { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class ContentReportListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<ContentReportItemDto> Items { get; set; } = [];
    }

    public sealed class ContentReportItemDto
    {
        public long Id { get; set; }

        public required ReportedContentType ContentType { get; set; }

        public long ContentId { get; set; }

        /// <summary>The item's title (a question's code) when it lives on this backend; null for multimedia and Q&amp;A.</summary>
        public string? ContentTitle { get; set; }

        public byte ReportType { get; set; }

        public string? Message { get; set; }

        /// <summary>The reporter's name (no email: the list may be delegated).</summary>
        public string? UserName { get; set; }

        public required ContentReportStatus Status { get; set; }

        public string? AdminNote { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? ClosedDate { get; set; }
    }
}
