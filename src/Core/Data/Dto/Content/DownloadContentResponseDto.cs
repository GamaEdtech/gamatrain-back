namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadContentResponseDto
    {
        /// <summary>Null when the download didn't happen (e.g. insufficient balance).</summary>
        public string? Url { get; set; }
        public string? Name { get; set; }

        /// <summary>Whether the downloader was charged for this download (false if gama-api already reported it as paid).</summary>
        public bool Spent { get; set; }
        public SpendSource? PaidBy { get; set; }

        /// <summary>Why quota wasn't consumed - null when <see cref="Spent"/>. See <see cref="ConsumeQuotaResponseDto.Reason"/>.</summary>
        public QuotaFailureReason? Reason { get; set; }

        /// <summary>See <see cref="ConsumeQuotaResponseDto.CurrentSubscriptionId"/> - null when the caller has no active subscription.</summary>
        public long? CurrentSubscriptionId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public long? CurrentPlanId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public string? CurrentPlanTitle { get; set; }

        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }

        /// <summary>The distinct <see cref="BillingInterval"/> names present anywhere in <see cref="UpgradeSuggestions"/>, in interval order.</summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }

        /// <summary>
        /// gama-api rejected the caller's own forwarded legacy token while resolving the download URL - see
        /// <c>GetDownloadUrlResponseDto.LegacyAuthRejected</c>. <c>DownloadsController</c> propagates this as a
        /// real HTTP 401 (a scoped exception to this API's usual "always 200, check succeeded/errors" convention
        /// - see CLAUDE.md), same as <c>IdentitiesController.GetDashboard</c> already does for the same failure
        /// shape, so the frontend's existing 401/403 interceptor re-authenticates the user the same way it
        /// already does everywhere else. <see cref="Spent"/> is always false here even when a charge was made
        /// and then reversed (see <c>ContentDeliveryService.RefundFailedDownloadAsync</c>) - the caller was not
        /// left net-charged for undelivered content, and this field reports that net outcome.
        /// </summary>
        public bool LegacyAuthRejected { get; set; }
    }
}
