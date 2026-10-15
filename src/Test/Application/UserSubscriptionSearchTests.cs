namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Specification.Subscription;

    using Xunit;

    // The admin subscription list search (GET admin/subscriptions/users?search=): subscriber email, first, last or full name.
    public class UserSubscriptionSearchTests
    {
        private static readonly UserSubscription Subscription = new()
        {
            User = new ApplicationUser { Email = "jane@x.com", FirstName = "Jane", LastName = "Doe" },
        };

        private static bool Matches(string term) => new UserSubscriptionSearchSpecification(term).Expression().Compile()(Subscription);

        [Theory]
        [InlineData("jane@x.com")]
        [InlineData("@x.com")]
        [InlineData("Jane")]
        [InlineData("Doe")]
        [InlineData("Jane Doe")]
        [InlineData("  Jane Doe  ")]
        public void SearchMatchesEmailOrName(string term) => Assert.True(Matches(term));

        [Theory]
        [InlineData("john")]
        [InlineData("Doe Jane")]
        public void SearchMissesUnrelatedTerm(string term) => Assert.False(Matches(term));
    }
}
