namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class AdminPostCommentsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? Status { get; set; }

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }

        [Display]
        [EmailAddress]
        public string? CommenterEmail { get; set; }

        [Display]
        public string? CommenterName { get; set; }
    }
}
