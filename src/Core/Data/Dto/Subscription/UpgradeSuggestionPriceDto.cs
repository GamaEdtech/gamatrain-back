namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class UpgradeSuggestionPriceDto
    {
        public BillingInterval? BillingInterval { get; set; }
        public Currency? Currency { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal? Price { get; set; }

        /// <summary><see cref="Price"/> normalized to a per-month cost, using <see cref="BillingInterval.Days"/>. Always set alongside <see cref="Price"/>.</summary>
        public decimal? MonthlyEquivalentPrice { get; set; }

        /// <summary>
        /// Savings vs. this same plan's Monthly price, e.g. <c>25</c> = 25% cheaper per month than paying Monthly.
        /// <see langword="null"/> when this entry is itself the Monthly price, or the plan has no Monthly price to compare against.
        /// </summary>
        public decimal? DiscountPercent { get; set; }
    }
}
