namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Referral;

    using Xunit;

    /// <summary>Pure tests of the teacher invite program rules: the admin percent, the offer lifetime, the percent locked on join, and the share.</summary>
    public class ReferralTermsTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void DefaultsApplyWhenNeverSet()
        {
            var settings = new ApplicationSettingsDto();

            Assert.Equal(ReferralTerms.DefaultPercent, ReferralTerms.Percent(settings));
            Assert.Equal(Now.AddDays(ReferralTerms.DefaultOfferValidDays), ReferralTerms.OfferExpiration(settings, Now));
        }

        [Theory]
        [InlineData(-5, 0)]
        [InlineData(150, 100)]
        [InlineData(12.5, 12.5)]
        public void PercentIsClamped(decimal stored, decimal expected) =>
            Assert.Equal(expected, ReferralTerms.Percent(new ApplicationSettingsDto { ReferralCommissionPercent = stored }));

        [Fact]
        public void ZeroOfferDaysMeansTheOfferNeverExpires()
        {
            var expiration = ReferralTerms.OfferExpiration(new ApplicationSettingsDto { ReferralOfferValidDays = 0 }, Now);

            Assert.Null(expiration);
            Assert.True(ReferralTerms.OfferOpen(expiration, Now.AddYears(5)));
        }

        // The promise in the email: joining before the offer expires locks the offered percent, even after the admin lowered it.
        [Fact]
        public void JoiningWhileTheOfferIsOpenLocksTheOfferedPercent() =>
            Assert.Equal(15m, ReferralTerms.PercentOnJoin(offerPercent: 15m, offerExpiration: Now.AddDays(1), currentPercent: 10m, Now));

        [Fact]
        public void JoiningAfterTheOfferExpiredLocksTheCurrentPercent() =>
            Assert.Equal(10m, ReferralTerms.PercentOnJoin(offerPercent: 15m, offerExpiration: Now.AddDays(-1), currentPercent: 10m, Now));

        [Fact]
        public void TheExpiryMomentItselfIsStillOpen() =>
            Assert.True(ReferralTerms.OfferOpen(Now, Now));

        [Theory]
        [InlineData("USD", 9.99, null, 9.99)]
        [InlineData("USDC", 20.0, null, 20.0)]
        [InlineData("SOL", 3.0, null, null)]
        [InlineData("SOL", 3.0, 450.5, 450.5)]
        public void PaymentUsdUsesTheBaseAmountOrAUsdPeggedAmount(string currency, double amount, double? baseAmount, double? expected) =>
            Assert.Equal(expected is null ? null : (decimal)expected, ReferralTerms.PaymentUsd(currency, (decimal)amount, baseAmount is null ? null : (decimal)baseAmount));

        [Theory]
        [InlineData(9.99, 15, 1.4985)]
        [InlineData(199, 15, 29.85)]
        [InlineData(89.88, 12.5, 11.235)]
        public void ShareIsThePercentOfThePayment(double paymentUsd, double percent, double expected) =>
            Assert.Equal((decimal)expected, ReferralTerms.Share((decimal)paymentUsd, (decimal)percent));

        [Theory]
        [InlineData("Sara", "Karimi", "Sara K.")]
        [InlineData("Sara", null, "Sara")]
        [InlineData(null, "Karimi", "K.")]
        [InlineData(" ", "", "New user")]
        public void InvitedUsersShowOnlyAFirstNameAndInitial(string? first, string? last, string expected) =>
            Assert.Equal(expected, ReferralService.ShortName(first, last));

        [Theory]
        [InlineData(" abcDEF1234 ", "abcDEF1234")]
        [InlineData("", null)]
        [InlineData("waytoolongcode", null)]
        public void InviteCodesAreTrimmedAndBounded(string input, string? expected) =>
            Assert.Equal(expected, ReferralService.NormalizeCode(input));

        [Fact]
        public void IdentitiesMatchIgnoringCaseAndSpaces() =>
            Assert.Equal(ReferralService.NormalizeIdentity(" Sara@Example.com"), ReferralService.NormalizeIdentity("sara@example.com "));
    }
}
