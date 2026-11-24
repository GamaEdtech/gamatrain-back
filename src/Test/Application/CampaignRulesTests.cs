namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Data.Dto.Campaign;

    using Xunit;

    // Sale campaign rules: when one is live, yearly runs, overlaps, field checks, and which discount wins.
    public class CampaignRulesTests
    {
        private static readonly DateTimeOffset Starts = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Ends = new(2026, 11, 29, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void ALiveWindowIncludesItsStartAndExcludesItsEnd()
        {
            Assert.False(CampaignRules.IsLive(true, Starts, Ends, false, Starts.AddSeconds(-1)));
            Assert.True(CampaignRules.IsLive(true, Starts, Ends, false, Starts));
            Assert.True(CampaignRules.IsLive(true, Starts, Ends, false, Ends.AddSeconds(-1)));
            Assert.False(CampaignRules.IsLive(true, Starts, Ends, false, Ends));
            Assert.False(CampaignRules.IsLive(false, Starts, Ends, false, Starts.AddDays(1)));
        }

        [Fact]
        public void AOneOffCampaignIsOverForGood()
        {
            Assert.Null(CampaignRules.Occurrence(Starts, Ends, false, Ends.AddYears(1).AddDays(-2)));
            Assert.False(CampaignRules.IsLive(true, Starts, Ends, false, Starts.AddYears(1).AddDays(1)));
        }

        [Fact]
        public void AYearlyCampaignComesBackOnTheSameDays()
        {
            // Between runs: the next one is next year's.
            var run = CampaignRules.Occurrence(Starts, Ends, true, new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Equal((Starts.AddYears(1), Ends.AddYears(1)), run);
            Assert.False(CampaignRules.IsLive(true, Starts, Ends, true, new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero)));

            // Live again in 2027 and in 2030, never in between.
            Assert.True(CampaignRules.IsLive(true, Starts, Ends, true, new DateTimeOffset(2027, 11, 25, 12, 0, 0, TimeSpan.Zero)));
            Assert.True(CampaignRules.IsLive(true, Starts, Ends, true, new DateTimeOffset(2030, 11, 28, 23, 0, 0, TimeSpan.Zero)));
            Assert.False(CampaignRules.IsLive(true, Starts, Ends, true, new DateTimeOffset(2030, 11, 29, 0, 0, 0, TimeSpan.Zero)));
            Assert.Equal((Starts.AddYears(5), Ends.AddYears(5)), CampaignRules.Occurrence(Starts, Ends, true, new DateTimeOffset(2030, 12, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        [Fact]
        public void OverlapsCountEveryYearlyRun()
        {
            var now = Starts.AddDays(-10);
            var christmas = (new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 27, 0, 0, 0, TimeSpan.Zero), true);
            Assert.False(CampaignRules.OverlapsFrom((Starts, Ends, true), christmas, now));

            // A one-off sale next November runs into this year's yearly Black Friday window.
            var nextNovember = (new DateTimeOffset(2027, 11, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 12, 2, 0, 0, 0, TimeSpan.Zero), false);
            Assert.True(CampaignRules.OverlapsFrom((Starts, Ends, true), nextNovember, now));
            Assert.False(CampaignRules.OverlapsFrom((Starts, Ends, false), nextNovember, now));
        }

        [Fact]
        public void FieldsAreChecked()
        {
            Assert.Null(CampaignRules.Validate("Black Friday", null, 30, Starts, Ends, true));
            Assert.Equal("title", CampaignRules.Validate("  ", null, 30, Starts, Ends, false)?.Reference);
            Assert.Equal("discountPercent", CampaignRules.Validate("Sale", null, 0, Starts, Ends, false)?.Reference);
            Assert.Equal("discountPercent", CampaignRules.Validate("Sale", null, 91, Starts, Ends, false)?.Reference);
            Assert.Equal("endsAt", CampaignRules.Validate("Sale", null, 30, Ends, Starts, false)?.Reference);
            Assert.Equal("endsAt", CampaignRules.Validate("Sale", null, 30, Starts, Starts.AddDays(200), true)?.Reference);
            Assert.Null(CampaignRules.Validate("Sale", null, 30, Starts, Starts.AddDays(200), false));
        }

        [Theory]
        [InlineData(null, null, null, false)]
        [InlineData(20, null, 20, false)]
        [InlineData(null, 30, 30, true)]
        [InlineData(20, 30, 30, true)]
        [InlineData(30, 30, 30, false)]
        [InlineData(40, 30, 40, false)]
        public void TheLargerDiscountWinsAndTheyNeverStack(int? recovery, int? campaign, int? expected, bool fromCampaign) =>
            Assert.Equal((expected, fromCampaign), CampaignRules.PickDiscount(recovery, campaign));
    }
}
