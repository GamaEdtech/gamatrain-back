namespace GamaEdtech.Presentation.ViewModel.Tutorial
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>A tutorial as the admin saves it: the title, the HTML (images uploaded through <c>EditorImages</c>), the
    /// board and subject are required; topics are gama-api topic ids; the description is made from the headings when empty.</summary>
    public sealed class TutorialRequestViewModel
    {
        [Display]
        [Required]
        [StringLength(1000)]
        public string? Title { get; set; }

        [Display]
        [Required]
        public string? Content { get; set; }

        [Display]
        [StringLength(1000)]
        public string? Description { get; set; }

        [Display]
        [Required]
        public int? BoardId { get; set; }

        [Display]
        public int? GradeId { get; set; }

        [Display]
        public int? CourseId { get; set; }

        [Display]
        [Required]
        public int? SubjectId { get; set; }

        [Display]
        public IReadOnlyList<int>? TopicIds { get; set; }

        [Display]
        public IReadOnlyList<string>? Keywords { get; set; }

        /// <summary>Unchanged when omitted; a new tutorial is <c>Pending</c> by default.</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<TutorialStatus, byte>))]
        public TutorialStatus? Status { get; set; }
    }

    public sealed class TutorialStatusRequestViewModel
    {
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<TutorialStatus, byte>))]
        public TutorialStatus? Status { get; set; }
    }

    public sealed class TutorialListResponseViewModel
    {
        public int Total { get; set; }

        public IEnumerable<TutorialItemResponseViewModel> Items { get; set; } = [];
    }

    public class TutorialItemResponseViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        [JsonConverter(typeof(EnumerationConverter<TutorialStatus, byte>))]
        public TutorialStatus? Status { get; set; }

        public int? BoardId { get; set; }

        public string? BoardTitle { get; set; }

        public int? GradeId { get; set; }

        public string? GradeTitle { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public string? SubjectTitle { get; set; }

        public IEnumerable<TutorialTopicViewModel> Topics { get; set; } = [];

        public IEnumerable<string> Keywords { get; set; } = [];

        public long? OwnerUserId { get; set; }

        public string? OwnerName { get; set; }

        public int ViewCount { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? LastModifyDate { get; set; }
    }

    public sealed class TutorialResponseViewModel : TutorialItemResponseViewModel
    {
        public string? Content { get; set; }

        public string? Description { get; set; }
    }

    public sealed class TutorialTopicViewModel
    {
        public int Id { get; set; }

        public string? Title { get; set; }
    }

    public sealed class TutorialKeywordsResponseViewModel
    {
        public IEnumerable<string> Keywords { get; set; } = [];

        public IEnumerable<string> Clusters { get; set; } = [];
    }
}
