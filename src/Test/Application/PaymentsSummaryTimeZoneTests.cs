namespace GamaEdtech.Test.Application
{
    using System.Linq.Expressions;

    using GamaEdtech.Data.Dto.Payment;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.Payment;

    using Xunit;

    /// <summary>
    /// Pure in-memory tests of the time zone handling behind <c>GET finance/payments/summary</c>: which local
    /// day a UTC bucket belongs to, and which UTC instants a local StartDate/EndDate range covers.
    /// </summary>
    public class PaymentsSummaryTimeZoneTests
    {
        private static readonly DateTime Sep30 = Day(2026, 9, 30);
        private static readonly DateTime Oct1 = Day(2026, 10, 1);

        // 22:30 UTC on Sep 30 = 02:00 Oct 1 in Tehran, 01:30 Oct 1 in Istanbul, 18:30 Sep 30 in New York.
        [Theory]
        [InlineData("Asia/Tehran", 1)]
        [InlineData("Europe/Istanbul", 1)]
        [InlineData("America/New_York", 30)]
        [InlineData("UTC", 30)]
        public void LateEveningUtcBucketFallsOnTheViewersOwnDay(string timeZoneId, int expectedDay)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

            Assert.Equal(expectedDay, PaymentsSummaryDto.ToLocalDate(Sep30, 22, 2, zone).Day);
        }

        // Tehran is UTC+03:30: local midnight is 20:30 UTC, i.e. the quarter-hour bucket starting 20:30.
        [Fact]
        public void HalfHourOffsetSplitsTheDayOnTheQuarterHourBucket()
        {
            var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

            Assert.Equal(Sep30, PaymentsSummaryDto.ToLocalDate(Sep30, 20, 1, tehran));
            Assert.Equal(Oct1, PaymentsSummaryDto.ToLocalDate(Sep30, 20, 2, tehran));
        }

        // New York leaves DST on 2026-11-01: midnight is 04:00 UTC that day but 05:00 UTC the next.
        [Fact]
        public void DstChangeInsideTheRangeUsesEachDaysOwnOffset()
        {
            var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

            Assert.Equal(Day(2026, 11, 1), PaymentsSummaryDto.ToLocalDate(Day(2026, 11, 1), 4, 0, newYork));
            Assert.Equal(Day(2026, 11, 1), PaymentsSummaryDto.ToLocalDate(Day(2026, 11, 2), 4, 3, newYork));
            Assert.Equal(Day(2026, 11, 2), PaymentsSummaryDto.ToLocalDate(Day(2026, 11, 2), 5, 0, newYork));
        }

        [Fact]
        public void DateRangeCoversTheLocalDayNotTheUtcDay()
        {
            var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            var day = new DateOnly(2026, 10, 1);
            var inRange = Compile(new CreationDateBetweenSpecification(day, day, tehran).Expression());

            Assert.False(inRange(At(2026, 9, 30, 20, 29)));
            Assert.True(inRange(At(2026, 9, 30, 20, 30)));
            Assert.True(inRange(At(2026, 10, 1, 20, 29)));
            Assert.False(inRange(At(2026, 10, 1, 20, 30)));
        }

        [Fact]
        public void DateRangeWithoutTimeZoneStaysUtc()
        {
            var day = new DateOnly(2026, 10, 1);
            var inRange = Compile(new CreationDateBetweenSpecification(day, day).Expression());

            Assert.False(inRange(At(2026, 9, 30, 23, 59)));
            Assert.True(inRange(At(2026, 10, 1, 0, 0)));
            Assert.True(inRange(At(2026, 10, 1, 23, 59)));
            Assert.False(inRange(At(2026, 10, 2, 0, 0)));
        }

        private static Func<DateTimeOffset, bool> Compile(Expression<Func<Payment, bool>> expression)
        {
            var predicate = expression.Compile();

            return creationDate => predicate(new Payment { CreationDate = creationDate });
        }

        private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

        private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
            new(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }
}
