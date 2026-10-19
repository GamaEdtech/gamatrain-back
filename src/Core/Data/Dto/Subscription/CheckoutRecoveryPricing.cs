namespace GamaEdtech.Data.Dto.Subscription
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Dto.ApplicationSettings;

    /// <summary>
    /// Checkout recovery offer rules (2026-10-19), the one place they live: the admin-set discount and offer lifetime
    /// (ApplicationSettings, defaults below when never set) and the discounted first payment.
    /// </summary>
    public static class CheckoutRecoveryPricing
    {
        public const int DefaultDiscountPercent = 20;
        public const int DefaultOfferDays = 7;

        /// <summary>The discount a new offer carries: the stored setting clamped to 1-90, or the default.</summary>
        public static int DiscountPercent([NotNull] ApplicationSettingsDto settings) =>
            Math.Clamp(settings.CheckoutRecoveryDiscountPercent ?? DefaultDiscountPercent, 1, 90);

        /// <summary>How long a new offer stays redeemable: the stored setting clamped to 1-30 days, or the default.</summary>
        public static int OfferDays([NotNull] ApplicationSettingsDto settings) =>
            Math.Clamp(settings.CheckoutRecoveryOfferDays ?? DefaultOfferDays, 1, 30);

        /// <summary>
        /// The first payment after <paramref name="discountPercent"/> off <paramref name="price"/>, in the same cents
        /// Stripe charges: Stripe rounds a percent-off coupon's discount to the nearest cent.
        /// </summary>
        public static decimal DiscountedPrice(decimal price, int discountPercent) =>
            price - Math.Round(price * discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
