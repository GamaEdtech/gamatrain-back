namespace GamaEdtech.Test.Application
{
    using System.Security.Claims;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.Ticket;

    using Xunit;

    // A ticket is the caller's when its UserId is theirs, or when its Email is theirs AND confirmed. Public sign-up
    // doesn't confirm the email, so an unconfirmed address must never open someone else's (e.g. anonymous) tickets.
    public class TicketAccessTests
    {
        private const long CallerId = 7;
        private const long OtherId = 8;

        private static ClaimsPrincipal User(string? email, bool? emailConfirmed)
        {
            List<Claim> claims = [new(ClaimTypes.NameIdentifier, CallerId.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            if (email is not null)
            {
                claims.Add(new(ClaimTypes.Email, email));
            }

            if (emailConfirmed.HasValue)
            {
                claims.Add(new(Constants.EmailConfirmedClaim, emailConfirmed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return new(new ClaimsIdentity(claims, "test"));
        }

        private static bool Matches(ClaimsPrincipal user, Ticket ticket) =>
            new UserTicketsSpecification(user).Expression().Compile()(ticket);

        [Theory]
        [InlineData("Jane@X.com", true, "jane@x.com")]
        [InlineData("jane@x.com", false, null)]
        [InlineData("jane@x.com", null, null)]
        [InlineData("", true, null)]
        [InlineData(null, true, null)]
        public void ConfirmedEmailOnlyWhenConfirmed(string? email, bool? confirmed, string? expected) =>
            Assert.Equal(expected, User(email, confirmed).ConfirmedEmail());

        [Fact]
        public void OwnTicketMatchesByUserIdEvenWithUnconfirmedEmail() =>
            Assert.True(Matches(User("jane@x.com", false), new Ticket { UserId = CallerId, Email = "someone@else.com" }));

        [Fact]
        public void AnonymousTicketMatchesByConfirmedEmail() =>
            Assert.True(Matches(User("Jane@x.com", true), new Ticket { UserId = null, Email = "jane@x.com" }));

        [Fact]
        public void AnonymousTicketDoesNotMatchUnconfirmedEmail() =>
            Assert.False(Matches(User("jane@x.com", false), new Ticket { UserId = null, Email = "jane@x.com" }));

        [Fact]
        public void OtherUsersTicketDoesNotMatch() =>
            Assert.False(Matches(User("jane@x.com", false), new Ticket { UserId = OtherId, Email = "bob@x.com" }));

        [Fact]
        public void RepliesAreScopedTheSameWay()
        {
            var ticket = new Ticket { UserId = null, Email = "jane@x.com" };
            var reply = new TicketReply { TicketId = 1, Ticket = ticket };

            Assert.False(new UserTicketReplysSpecification(1, User("jane@x.com", false)).Expression().Compile()(reply));
            Assert.True(new UserTicketReplysSpecification(1, User("jane@x.com", true)).Expression().Compile()(reply));
            Assert.False(new UserTicketReplysSpecification(2, User("jane@x.com", true)).Expression().Compile()(reply));
        }
    }
}
