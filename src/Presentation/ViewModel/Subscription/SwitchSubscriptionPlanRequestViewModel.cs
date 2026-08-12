namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SwitchSubscriptionPlanRequestViewModel
    {
        [Display]
        [Required]
        public long? SubscriptionPlanId { get; set; }
    }
}
