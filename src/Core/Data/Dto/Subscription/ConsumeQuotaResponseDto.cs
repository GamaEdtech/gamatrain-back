namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// A non-consumed result is not an error: the operation reports why (so callers can fall back
    /// to other charging, e.g. wallet points) and which plans would cover the feature.
    /// </summary>
    public sealed class ConsumeQuotaResponseDto
    {
        public bool Consumed { get; set; }
        public QuotaFailureReason? Reason { get; set; }
        public int? RemainingQuota { get; set; }

        /// <summary>
        /// Two distinct meanings depending on <see cref="Consumed"/>. When <see langword="false"/>: the caller's
        /// own existing Active subscription (earliest-expiring, if they happen to have more than one - see
        /// docs/business/subscriptions.md's "Quota consumption and the points fallback"), or <see langword="null"/>
        /// when they have none (<see cref="QuotaFailureReason.NoActiveSubscription"/>). Added 2026-08-15
        /// specifically so a client acting on <see cref="UpgradeSuggestions"/> can tell whether the right next
        /// call is "switch my existing subscription" (this is non-null) or "purchase a fresh one" (this is null).
        /// When <see langword="true"/> (added 2026-09-03): the subscription that was actually just charged -
        /// callers that need to reverse this exact consumption later (e.g. <c>ContentDeliveryService</c>, when
        /// the content it just paid for turns out to never actually deliver) pass this back to
        /// <c>ISubscriptionQuotaService.RefundQuotaAsync</c>.
        /// </summary>
        public long? CurrentSubscriptionId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public long? CurrentPlanId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public string? CurrentPlanTitle { get; set; }

        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }

        /// <summary>
        /// The distinct <see cref="BillingInterval"/> names present anywhere in <see cref="UpgradeSuggestions"/>
        /// (e.g. <c>["Monthly", "Annual"]</c>), in interval order - a ready-made tab/period manifest so the caller
        /// doesn't have to scan every suggested plan's prices to know which periods exist.
        /// </summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
