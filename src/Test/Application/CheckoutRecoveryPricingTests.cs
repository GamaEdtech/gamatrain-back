namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Subscription;

    using Xunit;

    /// <summary>Pure tests of the checkout recovery offer rules: the admin-set discount/lifetime and the discounted first payment.</summary>
    public class CheckoutRecoveryPricingTests
    {
        [Fact]
        public void DefaultsApplyWhenNeverSet()
        {
            var settings = new ApplicationSettingsDto();

            Assert.Equal(CheckoutRecoveryPricing.DefaultDiscountPercent, CheckoutRecoveryPricing.DiscountPercent(settings));
            Assert.Equal(CheckoutRecoveryPricing.DefaultOfferDays, CheckoutRecoveryPricing.OfferDays(settings));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(100, 90)]
        [InlineData(35, 35)]
        public void DiscountIsClamped(int stored, int expected) =>
            Assert.Equal(expected, CheckoutRecoveryPricing.DiscountPercent(new ApplicationSettingsDto { CheckoutRecoveryDiscountPercent = stored }));

        [Theory]
        [InlineData(0, 1)]
        [InlineData(365, 30)]
        public void OfferDaysAreClamped(int stored, int expected) =>
            Assert.Equal(expected, CheckoutRecoveryPricing.OfferDays(new ApplicationSettingsDto { CheckoutRecoveryOfferDays = stored }));

        // Stripe rounds a percent-off discount to the nearest cent; the recorded first payment must be what was charged.
        [Theory]
        [InlineData("9.99", 20, "7.99")]   // 1.998 off -> 2.00
        [InlineData("10.00", 20, "8.00")]
        [InlineData("4.99", 15, "4.24")]   // 0.7485 off -> 0.75
        [InlineData("99.00", 33, "66.33")] // 32.67 off
        public void DiscountedPriceMatchesStripeCents(string price, int percent, string expected) =>
            Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                CheckoutRecoveryPricing.DiscountedPrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), percent));
    }
}
