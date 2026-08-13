namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ManageSubscriptionPlanRequestViewModel
    {
        [Display]
        public string? Title { get; set; }

        [Display]
        public IEnumerable<CoordinateViewModel>? Polygon { get; set; }

        [Display]
        public bool? IsActive { get; set; }

        [Display]
        public bool? Highlight { get; set; }
    }
}
