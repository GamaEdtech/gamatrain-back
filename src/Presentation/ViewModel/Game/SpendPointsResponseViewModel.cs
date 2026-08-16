namespace GamaEdtech.Presentation.ViewModel.Game
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SpendPointsResponseViewModel
    {
        public bool Spent { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SpendSource, byte>))]
        public SpendSource? PaidBy { get; set; }

        public int? RemainingQuota { get; set; }

        /// <summary>Why quota wasn't consumed - null when <see cref="Spent"/>.</summary>
        [JsonConverter(typeof(EnumerationConverter<QuotaFailureReason, byte>))]
        public QuotaFailureReason? Reason { get; set; }

        /// <summary>The caller's own existing active subscription, if any - null means they have none, so the
        /// right next call for an upgrade-suggestion click-through is a fresh purchase, not a switch.</summary>
        public long? CurrentSubscriptionId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public long? CurrentPlanId { get; set; }

        /// <summary>Paired with <see cref="CurrentSubscriptionId"/> - null exactly when that is.</summary>
        public string? CurrentPlanTitle { get; set; }

        /// <summary>One entry per suggested plan, each with up to the 3 cheapest prices per billing interval nested inside.</summary>
        public IEnumerable<UpgradeSuggestionViewModel>? UpgradeSuggestions { get; set; }

        /// <summary>The distinct billing-interval names present anywhere in <see cref="UpgradeSuggestions"/>, in interval order.</summary>
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
