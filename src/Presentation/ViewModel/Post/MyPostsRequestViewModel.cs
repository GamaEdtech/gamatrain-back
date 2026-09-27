namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class MyPostsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        /// <summary>Optional - omitting it returns posts of every status. Nullable on purpose: a non-nullable reference type would be implicitly required.</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? Status { get; set; }

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }
    }
}
