namespace GamaEdtech.Data.Dto.Game
{
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SpendPointsResponseDto
    {
        public bool Spent { get; set; }
        public SpendSource? PaidBy { get; set; }
        public int? RemainingQuota { get; set; }

        /// <summary>Why quota wasn't consumed - null when <see cref="Spent"/>. See <see cref="ConsumeQuotaResponseDto.Reason"/>.</summary>
        public QuotaFailureReason? Reason { get; set; }

        /// <summary>See <see cref="ConsumeQuotaResponseDto.CurrentSubscriptionId"/> - two meanings depending on <see cref="Spent"/>/<see cref="PaidBy"/>: when <see cref="PaidBy"/> is <see cref="SpendSource.SubscriptionQuota"/>, the subscription that was actually charged (pass to <c>IGameService.RefundPointsAsync</c> to reverse it); otherwise the caller's current subscription for upgrade-suggestion purposes, or null if they have none.</summary>
        public long? CurrentSubscriptionId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public long? CurrentPlanId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public string? CurrentPlanTitle { get; set; }

        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }

        /// <summary>The distinct <see cref="BillingInterval"/> names present anywhere in <see cref="UpgradeSuggestions"/>, in interval order.</summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
