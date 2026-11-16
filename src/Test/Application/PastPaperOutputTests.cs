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

        [Fact]
        public void DatesAreTheJalaliDayAsWritten()
        {
            Assert.Equal("1405/06/24", PastPaperOutput.Jalali(new DateTime(2026, 9, 15, 11, 46, 32, DateTimeKind.Unspecified)));
            Assert.Equal("1405/06/14", PastPaperOutput.Jalali(new DateTime(2026, 9, 5, 10, 23, 26, DateTimeKind.Unspecified)));
        }

        [Fact]
        public void AListShowsHowLongAgoForADayThenTheDate()
        {
            var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Unspecified);
            Assert.Equal("3 Hours قبل", PastPaperOutput.ListDate(now.AddHours(-3), now));
            Assert.Equal("1 Minute قبل", PastPaperOutput.ListDate(now.AddSeconds(-90), now));
            Assert.Equal("1405/06/22", PastPaperOutput.ListDate(now.AddDays(-2), now));
        }

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
