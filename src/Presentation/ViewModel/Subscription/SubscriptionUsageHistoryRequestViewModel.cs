namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SubscriptionUsageHistoryRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public long? UserId { get; set; }

        [Display]
        public string? FeatureCode { get; set; }

        /// <summary>Filter to consumption events for one specific content item (e.g. one pastpaper's id) - same filter shape as admin/transactions' own IdentifierId.</summary>
        [Display]
        public long? IdentifierId { get; set; }

        [Display]
        public DateTimeOffset? FromDate { get; set; }

        [Display]
        public DateTimeOffset? ToDate { get; set; }
    }
}
