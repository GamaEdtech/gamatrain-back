namespace GamaEdtech.Presentation.ViewModel.Post
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RandomPostsRequestViewModel
    {
        [Display]
        [Required]
        [Range(1, int.MaxValue)]
        public int Size { get; set; } = 15;
    }
}
