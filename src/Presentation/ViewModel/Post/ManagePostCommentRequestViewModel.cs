namespace GamaEdtech.Presentation.ViewModel.Post
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ManagePostCommentRequestViewModel
    {
        [Display]
        [Required]
        public string? Captcha { get; set; }

        [Display]
        [Required]
        public string? Comment { get; set; }
    }
}
