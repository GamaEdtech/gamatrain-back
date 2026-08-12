namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ExtendUserSubscriptionRequestViewModel
    {
        [Display]
        [Required]
        public int? Days { get; set; }
    }
}
