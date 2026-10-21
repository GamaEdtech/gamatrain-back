namespace GamaEdtech.Data.Dto.Referral
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Dto.ApplicationSettings;

    /// <summary>
    /// Teacher invite program rules (2026-10-21), the one place they live: the admin-set percent and offer lifetime
    /// (ApplicationSettings, defaults below when never set), which percent a teacher locks when joining, and the share of
    /// a payment. See docs/business/referrals.md.
    /// </summary>
    public static class ReferralTerms
    {
        public const decimal DefaultPercent = 15;
        public const int DefaultOfferValidDays = 30;
        public const decimal MaxPercent = 100;

        /// <summary>The percent offered to a teacher now: the stored setting clamped to 0-100, or the default.</summary>
        public static decimal Percent([NotNull] ApplicationSettingsDto settings) =>
            Math.Clamp(settings.ReferralCommissionPercent ?? DefaultPercent, 0, MaxPercent);

        /// <summary>When a new offer expires: offer date + the stored days (clamped to 0-365), or never when 0.</summary>
        public static DateTimeOffset? OfferExpiration([NotNull] ApplicationSettingsDto settings, DateTimeOffset offerDate)
        {
            var days = Math.Clamp(settings.ReferralOfferValidDays ?? DefaultOfferValidDays, 0, 365);
            return days == 0 ? null : offerDate.AddDays(days);
        }

        /// <summary>True while the offered percent can still be locked.</summary>
        public static bool OfferOpen(DateTimeOffset? offerExpiration, DateTimeOffset now) =>
            offerExpiration is null || now <= offerExpiration;

        /// <summary>The percent a teacher locks by joining now: the offered one while the offer is open, otherwise the current setting.</summary>
        public static decimal PercentOnJoin(decimal offerPercent, DateTimeOffset? offerExpiration, decimal currentPercent, DateTimeOffset now) =>
            OfferOpen(offerExpiration, now) ? offerPercent : currentPercent;

        /// <summary>
        /// A payment's value in USD, or null when it can't be known: the locked base-currency amount when the payment has
        /// one, else the amount itself for USD and the USD stablecoins (the reconciliation renewal doesn't store a base
        /// amount). Subscriptions are only sold in these today.
        /// </summary>
        public static decimal? PaymentUsd(string currencyName, decimal amount, decimal? baseCurrencyAmount) =>
            baseCurrencyAmount ?? (currencyName is "USD" or "USDC" or "USDT" ? amount : null);

        /// <summary>The teacher's share, rounded to 4 decimals like the ledger column.</summary>
        public static decimal Share(decimal paymentUsd, decimal percent) =>
            Math.Round(paymentUsd * percent / 100m, 4, MidpointRounding.AwayFromZero);
    }
}
