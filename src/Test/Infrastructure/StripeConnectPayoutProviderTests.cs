namespace GamaEdtech.Test.Infrastructure
{
    using System;

    using GamaEdtech.Infrastructure.Provider.Payout;

    using Stripe;

    using Xunit;

    // Found live 2026-10-21: the payout-account create was sent before Connect was enabled, and Stripe replayed that
    // failure for its idempotency key for 24 hours. The key now changes every 10 minutes, and that failure has its own message.
    public class StripeConnectPayoutProviderTests
    {
        private static readonly DateTimeOffset SlotStart = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

        [Fact]
        public void AccountKeySameSlotIsTheSame() =>
            Assert.Equal(
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(5, "GB", SlotStart),
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(5, "GB", SlotStart.AddMinutes(9).AddSeconds(59)));

        [Fact]
        public void AccountKeyNextSlotDiffers() =>
            Assert.NotEqual(
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(5, "GB", SlotStart),
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(5, "GB", SlotStart.AddMinutes(10)));

        [Theory]
        [InlineData(6, "GB")]
        [InlineData(5, "PL")]
        public void AccountKeyOtherUserOrCountryDiffers(long userId, string country) =>
            Assert.NotEqual(
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(5, "GB", SlotStart),
                StripeConnectPayoutProvider.CreateAccountIdempotencyKey(userId, country, SlotStart));

        [Fact]
        public void ToUserMessageConnectNotEnabledSaysStripePayoutsUnavailable()
        {
            var exc = new StripeException("x")
            {
                StripeError = new()
                {
                    Type = "invalid_request_error",
                    Message = "You can only create new accounts if you've signed up for Connect, which you can do at https://dashboard.stripe.com/connect.",
                },
            };

            Assert.Equal("Stripe payouts aren't available yet. Please use Bank / PayPal (manual) for now.", StripeConnectPayoutProvider.ToUserMessage(exc));
        }

        [Fact]
        public void ToUserMessageUnknownErrorStaysGeneric() =>
            Assert.Equal(
                "Stripe couldn't process this right now. Please try again later.",
                StripeConnectPayoutProvider.ToUserMessage(new StripeException("x") { StripeError = new() { Message = "Something else (acct_123)" } }));
    }
}
