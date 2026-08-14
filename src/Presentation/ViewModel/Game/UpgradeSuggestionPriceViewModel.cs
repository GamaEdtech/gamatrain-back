namespace GamaEdtech.Presentation.ViewModel.Game
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UpgradeSuggestionPriceViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? Currency { get; set; }

        public string? CurrencySymbol { get; set; }

        public decimal? Price { get; set; }

        /// <summary><see cref="Price"/> normalized to a per-month cost. Always set alongside <see cref="Price"/>.</summary>
        public decimal? MonthlyEquivalentPrice { get; set; }

        /// <summary>
        /// Savings vs. this same plan's Monthly price, e.g. <c>25</c> = 25% cheaper per month than paying Monthly.
        /// <see langword="null"/> when this entry is itself the Monthly price, or the plan has no Monthly price to compare against.
        /// </summary>
        public decimal? DiscountPercent { get; set; }

        /// <summary>The suggested plan's limit for the feature that failed, at this billing interval; <see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }

        /// <summary>
        /// Codes of the other feature(s) that share <see cref="Limit"/> with the one that failed, if any -
        /// e.g. <c>Limit</c> is a 500-download pool also covering <c>ExamDownload</c>, not just the
        /// <c>PastpaperDownload</c> the caller was blocked on. <see langword="null"/>/empty when unpooled.
        /// </summary>
        public IEnumerable<string>? PooledFeatureCodes { get; set; }

        /// <summary>
        /// Already resolved server-side: the pooled bucket's description (see <see cref="PooledFeatureCodes"/>)
        /// when the failed feature is pooled, otherwise that feature's own description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>The plan's full feature-group list at this billing interval, not just the group that triggered the suggestion.</summary>
        public IEnumerable<UpgradeSuggestionFeatureGroupViewModel>? FeatureGroups { get; set; }
    }
}
