namespace GamaEdtech.Presentation.ViewModel.Post
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PostLocalizedValueViewModel
    {
        [Display]
        [Required]
        public int? LanguageId { get; set; }

        [Display]
        public string? Title { get; set; }

        [Display]
        public string? Summary { get; set; }

        [Display]
        public string? Body { get; set; }
    }
}
