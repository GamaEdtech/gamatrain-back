namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class FeaturesRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; }
    }
}
