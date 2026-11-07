namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;

    using Xunit;

    public class TeacherDirectorySpecificationTests
    {
        private static readonly Func<ApplicationUser, bool> HasName = new HasNameSpecification().Expression().Compile();
        private static readonly Func<ApplicationUser, bool> Listed = new TeacherDirectorySpecification().Expression().Compile();

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
        [Fact]
        public void PublicNamedTeacherIsListed() =>
            Assert.True(Listed(User(nameof(Role.Teacher), ProfileVisibility.Public, "Jane")));

        [Fact]
        public void PublicStudentIsNotListed() =>
            Assert.False(Listed(User(nameof(Role.Student), ProfileVisibility.Public, "Sam")));

        [Fact]
        public void TeacherWithNoNameIsNotListed() =>
            Assert.False(Listed(User(nameof(Role.Teacher), ProfileVisibility.Public, null)));

        [Fact]
        public void PrivateTeacherIsNotListed() =>
            Assert.False(Listed(User(nameof(Role.Teacher), ProfileVisibility.Private, "Jane")));

        private static ApplicationUser User(string role, ProfileVisibility visibility, string? firstName) => new()
        {
            FirstName = firstName,
            ProfileVisibility = visibility,
            UserRoles = [new() { Role = new() { Name = role, NormalizedName = role.ToUpperInvariant() } }],
        };
    }
}
