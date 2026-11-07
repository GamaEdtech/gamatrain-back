namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Specification.Identity;

    using Xunit;

    public class TeacherDirectorySpecificationTests
    {
        private static readonly Func<ApplicationUser, bool> HasName = new HasNameSpecification().Expression().Compile();

        [Theory]
        [InlineData("Jane", "Doe")]
        [InlineData("Jane", null)]
        [InlineData(null, "Doe")]
        public void UserWithAFirstOrLastNameIsListed(string? firstName, string? lastName) =>
            Assert.True(HasName(new() { FirstName = firstName, LastName = lastName }));

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("  ", " ")]
        public void UserWithNoNameIsLeftOut(string? firstName, string? lastName) =>
            Assert.False(HasName(new() { FirstName = firstName, LastName = lastName }));
    }
}
