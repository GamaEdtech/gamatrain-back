namespace GamaEdtech.Presentation.ViewModel.Blog
{
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.AspNetCore.Http;

    public sealed class PostContributionViewModel
    {
        [Display]
        [Required]
        public string? Title { get; set; }

        [Display]
        [Required]
        public string? Summary { get; set; }

        [Display]
        [Required]
        public string? Body { get; set; }

        [Display]
        [FileSize(1024 * 1024 * 2)]//2MB
        [FileExtensions(Constants.ValidImageExtensions)]
        public IFormFile? Image { get; set; }

        public IEnumerable<int>? Tags { get; set; }
    }
}
