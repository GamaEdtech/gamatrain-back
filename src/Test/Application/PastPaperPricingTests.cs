namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;

    using Xunit;

    /// <summary>
    /// gama-api's past paper price rule, reproduced exactly. The cases are real prices gama-api shows (paper 45830: a
    /// 14-page PDF and Word file, referee score A, price multiplier 5) with its live base prices (pdf 50, doc/docx 100,
    /// mp3 30).
    /// </summary>
    public sealed class PastPaperPricingTests
    {
        private static readonly Dictionary<string, int> BasePrices = PastPaperPricing.BasePrices([("pdf", 50), ("doc,docx", 100), ("mp3", 30)]);

        [Theory]
        [InlineData("pdf", 14, 4, 5, 190)]
        [InlineData("docx", 14, 4, 5, 370)]
        [InlineData("PDF", 14, 4, 5, 190)]
        [InlineData(".pdf", 14, 4, 5, 190)]
        public void AMainFileCostsWhatGamaApiShows(string extension, int pages, int refereeScore, int multiplier, int expected) =>
            Assert.Equal(expected, PastPaperPricing.Price(BasePrices, extension, pages, refereeScore, multiplier));

        [Fact]
        public void AnExtraFileUsesItsTypesMultiplier()
        {
            // Paper 45830's extra file: an 8-page PDF whose type is free (multiplier 0), shown at 0.
            Assert.Equal(0, PastPaperPricing.Price(BasePrices, "pdf", 8, 4, PastPaperPricing.ExtraTypeMultiplier("{\"price_multiplier\":0}")));
            Assert.Equal(30, PastPaperPricing.Price(BasePrices, "pdf", 8, 4, PastPaperPricing.ExtraTypeMultiplier(null)));
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(3, 1.5)]
        [InlineData(4, 2)]
        [InlineData(5, 2)]
        [InlineData(0, 0)]
        public void TheRefereeFactorIsGamaApis(int refereeScore, double factor) =>
            Assert.Equal(factor, PastPaperPricing.RefereeFactor(refereeScore));

        [Fact]
        public void ALowScoreMakesAPdfFreeButAWordFileHalfPrice()
        {
            Assert.Equal(0, PastPaperPricing.Price(BasePrices, "pdf", 4, 1, 1));
            Assert.Equal(10, PastPaperPricing.Price(BasePrices, "docx", 4, 1, 1));
        }

        [Fact]
        public void AFileTypeWithoutABasePriceIsFree()
        {
            Assert.Equal(0, PastPaperPricing.Price(BasePrices, "zip", 10, 5, 3));
            Assert.Equal(0, PastPaperPricing.Price(BasePrices, null, 10, 5, 3));
        }

        [Theory]
        [InlineData(184.99, 180)]
        [InlineData(185, 190)]
        [InlineData(4.99, 0)]
        public void PricesRoundToTheNearestTenLikePhp(double price, int expected) =>
            Assert.Equal(expected, PastPaperPricing.RoundToTen(price));

        [Theory]
        [InlineData("{\"price_multiplier\":\"0.5\"}", 0.5)]
        [InlineData("{\"other\":1}", 1)]
        [InlineData("not json", 1)]
        [InlineData("[]", 1)]
        public void AnExtraTypesMultiplierIsReadFromItsMetadata(string metadata, double expected) =>
            Assert.Equal((decimal)expected, PastPaperPricing.ExtraTypeMultiplier(metadata));
    }
}
