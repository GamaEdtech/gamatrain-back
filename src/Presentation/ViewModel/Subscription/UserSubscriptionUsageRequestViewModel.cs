namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>The caller's own quota usage log (GET subscriptions/me/usage). Always newest first: a client sort or search is ignored.</summary>
    public sealed class UserSubscriptionUsageRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public string? FeatureCode { get; set; }

        [Display]
        public DateTimeOffset? FromDate { get; set; }

        [Display]
        public DateTimeOffset? ToDate { get; set; }
    }
}
