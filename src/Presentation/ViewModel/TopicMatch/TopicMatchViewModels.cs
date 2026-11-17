namespace GamaEdtech.Presentation.ViewModel.TopicMatch
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>A match a person decided between a GAMA topic and another board's topic (gama-api's type ids).</summary>
    public sealed class TopicMatchAddRequestViewModel
    {
        [Display]
        [Required]
        public int? GamaTopicId { get; set; }

        [Display]
        [Required]
        public int? ExternalTopicId { get; set; }

        /// <summary>0-100; 0 when omitted.</summary>
        [Display]
        public byte? Confidence { get; set; }

        [Display]
        [StringLength(2000)]
        public string? Reasoning { get; set; }
    }

    public sealed class TopicMatchListResponseViewModel
    {
        public int Total { get; set; }

        public IEnumerable<TopicMatchResponseViewModel> Items { get; set; } = [];
    }

    public sealed class TopicMatchSuggestResponseViewModel
    {
        public int Added { get; set; }

        public IEnumerable<TopicMatchResponseViewModel> Items { get; set; } = [];

        public IEnumerable<string> Errors { get; set; } = [];
    }

    public sealed class TopicMatchResponseViewModel
    {
        public long Id { get; set; }

        public byte Confidence { get; set; }

        public string? Reasoning { get; set; }

        public string? AiModel { get; set; }

        [JsonConverter(typeof(EnumerationConverter<TopicMatchStatus, byte>))]
        public TopicMatchStatus? Status { get; set; }

        public string? ReviewedBy { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public TopicPathViewModel? Gama { get; set; }

        public TopicPathViewModel? External { get; set; }
    }

    public sealed class TopicPathViewModel
    {
        public TopicPathItemViewModel? Board { get; set; }

        public TopicPathItemViewModel? Grade { get; set; }

        public TopicPathItemViewModel? Subject { get; set; }

        public TopicPathItemViewModel? Topic { get; set; }

        public TopicPathItemViewModel? Tutorial { get; set; }
    }

    public sealed class TopicPathItemViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }
    }
}
