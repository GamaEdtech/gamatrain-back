namespace GamaEdtech.Data.Dto.Tutorial
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The admin list's filters: all optional; any status unless one is given.</summary>
    public sealed class TutorialAdminListRequestDto
    {
        public TutorialStatus? Status { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public int? TopicId { get; set; }

        /// <summary>The title, description or keywords contain it.</summary>
        public string? Title { get; set; }

        public long? OwnerUserId { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class TutorialAdminListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<TutorialAdminItemDto> Items { get; set; } = [];
    }

    /// <summary>A tutorial in the admin list, with its type titles and owner's name.</summary>
    public class TutorialAdminItemDto
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public TutorialStatus? Status { get; set; }

        public int? BoardId { get; set; }

        public string? BoardTitle { get; set; }

        public int? GradeId { get; set; }

        public string? GradeTitle { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public string? SubjectTitle { get; set; }

        public IReadOnlyList<TutorialTopicDto> Topics { get; set; } = [];

        public IReadOnlyList<string> Keywords { get; set; } = [];

        public long? OwnerUserId { get; set; }

        public string? OwnerName { get; set; }

        public int ViewCount { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? LastModifyDate { get; set; }
    }

    /// <summary>One tutorial for the edit form: the list's fields plus its HTML (image links on this backend's storage,
    /// sanitized) and description.</summary>
    public sealed class TutorialAdminDto : TutorialAdminItemDto
    {
        public string? Content { get; set; }

        public string? Description { get; set; }
    }

    public sealed class TutorialTopicDto
    {
        public int Id { get; set; }

        public string? Title { get; set; }
    }

    /// <summary>A tutorial as the admin saves it. The board and subject are required; the grade, course and topics follow
    /// gama-api's types. The description is made from the content's headings when left empty; the status stays when null
    /// (a new tutorial waits as <c>Pending</c>).</summary>
    public sealed class TutorialEditDto
    {
        public string? Title { get; set; }

        public string? Content { get; set; }

        public string? Description { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public IReadOnlyList<int>? TopicIds { get; set; }

        public IReadOnlyList<string>? Keywords { get; set; }

        public TutorialStatus? Status { get; set; }
    }

    /// <summary>What the AI found in a tutorial: its keywords (saved on the tutorial) and its learning clusters (shown only).</summary>
    public sealed class TutorialKeywordsDto
    {
        public IReadOnlyList<string> Keywords { get; set; } = [];

        public IReadOnlyList<string> Clusters { get; set; } = [];
    }
}
