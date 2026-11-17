namespace GamaEdtech.Data.Dto.TopicMatch
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The review list's filters, all optional: a topic (on either side), the GAMA or other board's subject, the status.</summary>
    public sealed class TopicMatchListRequestDto
    {
        public int? TopicId { get; set; }

        public int? GamaSubjectId { get; set; }

        public int? ExternalSubjectId { get; set; }

        public TopicMatchStatus? Status { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 50;
    }

    public sealed class TopicMatchListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<TopicMatchDto> Items { get; set; } = [];
    }

    /// <summary>A suggested match with both topics' place (board, grade, subject, topic) and the GAMA topic's first tutorial.</summary>
    public sealed class TopicMatchDto
    {
        public long Id { get; set; }

        public byte Confidence { get; set; }

        public string? Reasoning { get; set; }

        public string? AiModel { get; set; }

        public TopicMatchStatus? Status { get; set; }

        public string? ReviewedBy { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public TopicPathDto Gama { get; set; } = new();

        public TopicPathDto External { get; set; } = new();
    }

    /// <summary>gama-api's curriculum path: board, grade, subject, topic (null where the types have none).</summary>
    public sealed class TopicPathDto
    {
        public TopicPathItemDto? Board { get; set; }

        public TopicPathItemDto? Grade { get; set; }

        public TopicPathItemDto Subject { get; set; } = new();

        public TopicPathItemDto Topic { get; set; } = new();

        /// <summary>The topic's first published tutorial (by its order), to open it while reviewing.</summary>
        public TopicPathItemDto? Tutorial { get; set; }
    }

    public sealed class TopicPathItemDto
    {
        public long Id { get; set; }

        public string? Title { get; set; }
    }

    /// <summary>A match a person decided: the GAMA topic and the other board's topic (each subject is its topic's parent).</summary>
    public sealed class TopicMatchAddDto
    {
        public int GamaTopicId { get; set; }

        public int ExternalTopicId { get; set; }

        public byte? Confidence { get; set; }

        public string? Reasoning { get; set; }
    }

    /// <summary>What the AI suggested for a topic: how many new matches were saved, the topic's matches, and the subjects the AI
    /// could not answer for.</summary>
    public sealed class TopicMatchSuggestResultDto
    {
        public int Added { get; set; }

        public IReadOnlyList<TopicMatchDto> Items { get; set; } = [];

        public IReadOnlyList<string> Errors { get; set; } = [];
    }
}
