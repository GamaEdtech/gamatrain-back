namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Data.Dto.Dashboard;

    using Xunit;

    /// <summary>Pure tests of the admin dashboard's date range rules and the self-invite IP hashing.</summary>
    public class DashboardRangeTests
    {
        private static readonly DateOnly Today = new(2026, 10, 22);

        [Fact]
        public void NoRangeMeansTheLast30DaysIncludingToday()
        {
            var (from, to) = DashboardRange.Resolve(null, null, Today);

            Assert.Equal(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero), from);
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), to);
            Assert.Equal(30, DashboardRange.Days(from, to).Count);
        }

        [Fact]
        public void ASingleDayIsOneBucket()
        {
            var (from, to) = DashboardRange.Resolve(Today, Today, Today);

            Assert.Equal(Today, Assert.Single(DashboardRange.Days(from, to)));
        }

        [Fact]
        public void ReversedDatesAreSwapped()
        {
            var (from, to) = DashboardRange.Resolve(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1), Today);

            Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), from);
            Assert.Equal(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), to);
        }

        [Fact]
        public void ATooLongRangeKeepsItsEndAndIsCutToMaxDays()
        {
            var (from, to) = DashboardRange.Resolve(new DateOnly(2020, 1, 1), Today, Today);

            Assert.Equal(DashboardRange.MaxDays, DashboardRange.Days(from, to).Count);
            Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), to);
        }

        [Fact]
        public void APreviousMonthIsExactlyThatMonth()
        {
            var (from, to) = DashboardRange.Resolve(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Today);

            Assert.Equal(30, DashboardRange.Days(from, to).Count);
        }

        [Fact]
        public void IpHashesMatchIgnoringCaseAndSpacesAndHideTheAddress()
        {
            var hash = DashboardRange.HashIp(" 2001:DB8::1 ");

            Assert.Equal(hash, DashboardRange.HashIp("2001:db8::1"));
            Assert.Equal(64, hash!.Length);
            Assert.DoesNotContain("2001", hash, StringComparison.Ordinal);
            Assert.NotEqual(hash, DashboardRange.HashIp("2001:db8::2"));
            Assert.Null(DashboardRange.HashIp("  "));
        }

        // Tehran is UTC+3:30 all year, so its 1 Nov runs from 20:30 UTC on 31 Oct to 20:30 UTC on 1 Nov.
        [Fact]
        public void ARangeIsWholeDaysInTheViewersTimeZone()
        {
            var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            var nov1 = new DateOnly(2026, 11, 1);

            var (from, to) = DashboardRange.Resolve(nov1, nov1, nov1, tehran);

            Assert.Equal(new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero), from.ToUniversalTime());
            Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 30, 0, TimeSpan.Zero), to.ToUniversalTime());
            Assert.Equal(nov1, Assert.Single(DashboardRange.Days(from, to, tehran)));
        }

        [Theory]
        [InlineData("Asia/Tehran", 1)]
        [InlineData("UTC", 31)]
        [InlineData("America/New_York", 31)]
        public void AnInstantFallsOnTheViewersOwnDay(string timeZoneId, int expectedDay)
        {
            // 21:00 UTC on 31 Oct = 00:30 on 1 Nov in Tehran, 17:00 on 31 Oct in New York.
            var instant = new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero);

            Assert.Equal(expectedDay, DashboardRange.LocalDay(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).Day);
        }

        [Fact]
        public void WithoutATimeZoneTheDaysStayUtc()
        {
            var (from, to) = DashboardRange.Resolve(Today, Today, Today);

            Assert.Equal(TimeSpan.Zero, from.Offset);
            Assert.Equal(new DateTimeOffset(Today, TimeOnly.MinValue, TimeSpan.Zero), from);
            Assert.Equal(Today, Assert.Single(DashboardRange.Days(from, to)));
        }
    }
}
