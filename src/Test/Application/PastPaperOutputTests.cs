namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;

    using Xunit;

    /// <summary>gama-api's output rules for a paper, checked against values gama-api's own answers show.</summary>
    public sealed class PastPaperOutputTests
    {
        [Theory]
        [InlineData("Predicted Paper Biology 9700 Paper 3 Advanced Practical Skills 1 for Oct/Nov 2026", "Predicted-Paper-Biology-9700-Paper-3-Advanced-Practical-Skills-1-for-OctNov-2026")]
        [InlineData("Urdu Second Language 3248/01 May June 2026", "Urdu-Second-Language-324801-May-June-2026")]
        [InlineData("Urdu Second Language 3248/01 October November 2025", "Urdu-Second-Language-324801-October-November-2025")]
        [InlineData("  Physics &amp; Maths -- (A/AS)  ", "Physics-amp-Maths-AAS")]
        [InlineData("فیزیک Physics", "Physics")]
        public void TheTitleUrlIsGamaApis(string title, string expected) => Assert.Equal(expected, PastPaperOutput.TitleUrl(title));

        [Theory]
        [InlineData(659_456, "644 KB")]
        [InlineData(2_097_152, "2 MB")]
        [InlineData(217_088, "212 KB")]
        [InlineData(1023, "1023 B")]
        [InlineData(1536, "2 KB")]
        public void SizesAreWrittenLikeGamaApi(long bytes, string expected) => Assert.Equal(expected, PastPaperOutput.FormatSize(bytes));

        [Theory]
        [InlineData("""{"season":1,"preTitle":"Unit 2"}""", false)]
        [InlineData("""{"season":true}""", true)]
        [InlineData("""{"season":"1","kind":"season"}""", true)]
        [InlineData("""{"season":0,"kind":"season"}""", false)]
        [InlineData(null, false)]
        [InlineData("not json", false)]
        public void ATopicIsASeasonAsGamaApiDecidesIt(string? metadata, bool expected) => Assert.Equal(expected, PastPaperOutput.IsSeason(metadata));

        [Theory]
        [InlineData(11, "Nov")]
        [InlineData(6, "Jun")]
        [InlineData(null, null)]
        [InlineData(0, null)]
        public void MonthsHaveTheirShortTitle(int? month, string? expected) => Assert.Equal(expected, PastPaperOutput.MonthTitle(month));

        [Fact]
        public void NumbersAreWrittenAsMySqlSendsThem()
        {
            Assert.Equal("5", PastPaperOutput.Number(5.0000m));
            Assert.Equal("1.5", PastPaperOutput.Number(1.5m));
            Assert.Equal("2026-09-15 11:46:32", PastPaperOutput.Written(new DateTimeOffset(2026, 9, 15, 11, 46, 32, TimeSpan.Zero)));
        }
    }
}
