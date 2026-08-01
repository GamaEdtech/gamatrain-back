namespace GamaEdtech.Presentation.ViewModel.Game
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    public sealed class UpgradeSuggestionViewModel
    {
        public long SubscriptionPlanId { get; set; }

        public string? Title { get; set; }

        /// <summary>The suggested plan's limit for the feature that failed; <see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        public bool Highlight { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        public string? CurrencySymbol { get; set; }

        public decimal? Price { get; set; }

        /// <summary>The plan's full feature/limit list, not just the one that triggered the suggestion.</summary>
        public IEnumerable<PlanFeatureViewModel>? Features { get; set; }
    }
}
