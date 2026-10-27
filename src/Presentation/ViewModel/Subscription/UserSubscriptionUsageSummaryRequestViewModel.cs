namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class UserSubscriptionUsageSummaryRequestViewModel
    {
        [Display]
        [Required]
        public DateTimeOffset? FromDate { get; set; }

        [Display]
        [Required]
        public DateTimeOffset? ToDate { get; set; }
    }
}
