namespace GamaEdtech.Presentation.ViewModel.Post
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RejectRequestViewModel
    {
        [Display]
        [Required]
        [StringLength(300)]
        public string? Comment { get; set; }
    }
}
