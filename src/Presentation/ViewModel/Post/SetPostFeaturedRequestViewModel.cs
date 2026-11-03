namespace GamaEdtech.Presentation.ViewModel.Post
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SetPostFeaturedRequestViewModel
    {
        [Display]
        public bool IsFeatured { get; set; }
    }
}
