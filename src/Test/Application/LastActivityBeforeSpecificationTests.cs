namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification;

    using Xunit;

    public class LastActivityBeforeSpecificationTests
    {
        private static readonly DateTimeOffset Cutoff = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly Func<Contribution, bool> Matches = new LastActivityBeforeSpecification(Cutoff).Expression().Compile();

        [Fact]
        public void OldLastModifyDateMatches() =>
            Assert.True(Matches(new() { CreationDate = Cutoff.AddDays(-60), LastModifyDate = Cutoff.AddDays(-1) }));

        [Fact]
        public void RecentLastModifyDateWinsOverOldCreationDate() =>
            Assert.False(Matches(new() { CreationDate = Cutoff.AddDays(-60), LastModifyDate = Cutoff.AddDays(1) }));

        [Fact]
        public void WithoutLastModifyDateCreationDateIsUsed()
        {
            Assert.True(Matches(new() { CreationDate = Cutoff.AddDays(-1) }));
            Assert.False(Matches(new() { CreationDate = Cutoff.AddDays(1) }));
        }
    }
}
