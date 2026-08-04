namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class UpgradeSuggestionDto
    {
        public long SubscriptionPlanId { get; set; }
        public string? Title { get; set; }

        /// <summary>The suggested plan's limit for the feature that failed; <see langword="null"/> means unlimited. Plan-wide - identical for every entry in <see cref="Prices"/>.</summary>
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

        public bool Highlight { get; set; }

        /// <summary>One entry per billing interval this plan was suggested at (up to the 3 cheapest per interval, cheapest first).</summary>
        public IEnumerable<UpgradeSuggestionPriceDto>? Prices { get; set; }

        /// <summary>The plan's full feature-group list, not just the group that triggered the suggestion.</summary>
        public IEnumerable<PlanFeatureGroupDto>? Features { get; set; }
    }
}
