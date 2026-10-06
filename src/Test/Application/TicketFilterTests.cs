namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.Ticket;

    using Xunit;

    // The admin ticket list filters (GET admin/tickets): unread, search, exact email, last-activity range.
    public class TicketFilterTests
    {
        private static readonly DateTimeOffset Day = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        private static bool Matches(SpecificationBase<Ticket> specification, Ticket ticket) => specification.Expression().Compile()(ticket);

        private static Ticket Ticket(long id = 1, bool read = true, params bool[] repliesReadByAdmin) => new()
        {
            Id = id,
            Subject = "Refund for annual plan",
            FullName = "Jane Doe",
            Email = "jane@x.com",
            IsReadByAdmin = read,
            LastActivityDate = Day,
            TicketReplys = [.. repliesReadByAdmin.Select(r => new TicketReply { IsReadByAdmin = r })],
        };

        [Theory]
        [InlineData("123", 123L)]
        [InlineData(" #123 ", 123L)]
        [InlineData("Ticket-123", 123L)]
        [InlineData("ticket-123", 123L)]
        [InlineData("[Ticket-123]", 123L)]
        [InlineData("0", null)]
        [InlineData("-5", null)]
        [InlineData("12a", null)]
        [InlineData("refund", null)]
        public void ParsesTicketNumber(string term, long? expected) => Assert.Equal(expected, TicketSearchSpecification.ParseTicketId(term));

        [Theory]
        [InlineData("Refund")]
        [InlineData("Jane")]
        [InlineData("@x.com")]
        [InlineData("Ticket-7")]
        public void SearchMatchesSubjectNameEmailOrId(string term) => Assert.True(Matches(new TicketSearchSpecification(term), Ticket(id: 7)));

        [Fact]
        public void SearchMissesUnrelatedTerm() => Assert.False(Matches(new TicketSearchSpecification("invoice"), Ticket(id: 7)));

        [Fact]
        public void SearchByOtherTicketNumberMisses() => Assert.False(Matches(new TicketSearchSpecification("Ticket-8"), Ticket(id: 7)));

        [Theory]
        [InlineData(false, new bool[0], true)]
        [InlineData(true, new[] { true, false }, true)]
        [InlineData(true, new[] { true, true }, false)]
        [InlineData(true, new bool[0], false)]
        public void UnreadMeansTicketOrAnyReplyUnread(bool read, bool[] replies, bool expected) =>
            Assert.Equal(expected, Matches(new TicketUnreadByAdminSpecification(), Ticket(read: read, repliesReadByAdmin: replies)));

        [Theory]
        [InlineData(false, new bool[0], false)]
        [InlineData(true, new[] { true, false }, false)]
        [InlineData(true, new[] { true, true }, true)]
        public void ReadMeansTicketAndAllRepliesRead(bool read, bool[] replies, bool expected) =>
            Assert.Equal(expected, new TicketUnreadByAdminSpecification().Not().Expression().Compile()(Ticket(read: read, repliesReadByAdmin: replies)));

        [Fact]
        public void EmailIsExactMatch()
        {
            Assert.True(Matches(new TicketEmailEqualsSpecification(" jane@x.com "), Ticket()));
            Assert.False(Matches(new TicketEmailEqualsSpecification("x.com"), Ticket()));
        }

        [Fact]
        public void LastActivityRangeIsInclusiveAndOpenEnded()
        {
            Assert.True(Matches(new LastActivityDateBetweenSpecification(Day, Day), Ticket()));
            Assert.True(Matches(new LastActivityDateBetweenSpecification(null, Day), Ticket()));
            Assert.True(Matches(new LastActivityDateBetweenSpecification(Day.AddDays(-1), null), Ticket()));
            Assert.False(Matches(new LastActivityDateBetweenSpecification(Day.AddSeconds(1), null), Ticket()));
            Assert.False(Matches(new LastActivityDateBetweenSpecification(null, Day.AddSeconds(-1)), Ticket()));
        }
    }
}
