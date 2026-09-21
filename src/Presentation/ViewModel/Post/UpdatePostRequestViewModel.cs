namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    public sealed class UpdatePostRequestViewModel
    {
        [Display]
        public string? Title { get; set; }

        [Display]
        public string? Slug { get; set; }

        [Display]
        public string? Summary { get; set; }

        [Display]
        public string? Body { get; set; }

        [Display]
        [FileSize(1024 * 1024 * 2)]//2MB
        [FileExtensions(Constants.ValidImageExtensions)]
        public IFormFile? Image { get; set; }

        [Display]
        [FileSize(1024 * 1024 * 10)]//10MB
        [FileExtensions("mp3")]
        public IFormFile? Podcast { get; set; }

        [Display]
        public bool RemovePodcast { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<VisibilityType, byte>))]
        public VisibilityType? VisibilityType { get; set; }

        [Display]
        public DateTimeOffset? PublishDate { get; set; }

        /// <summary>Ignored when an admin edits; a user's edit sends the post to review unless this is true.</summary>
        [Display]
        public bool? Draft { get; set; }

        [Display]
        public string? Keywords { get; set; }

        [Display]
        public IEnumerable<long>? Tags { get; set; }

        [Display]
        public IEnumerable<PostLocalizedValueViewModel>? LocalizedValues { get; set; }
    }
}
