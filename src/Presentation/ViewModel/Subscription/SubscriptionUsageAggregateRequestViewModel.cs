namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SubscriptionUsageAggregateRequestViewModel
    {
        /// <summary>Unset means "every user" - a global aggregate dashboard instead of one user's.</summary>
        [Display]
        public long? UserId { get; set; }

        [Display]
        [Required]
        public DateTimeOffset? FromDate { get; set; }

        [Display]
        [Required]
        public DateTimeOffset? ToDate { get; set; }
    }
}
