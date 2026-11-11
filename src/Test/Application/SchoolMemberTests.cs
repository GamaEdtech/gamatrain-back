namespace GamaEdtech.Test.Application
{
    using System;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.School;

    using Xunit;

    public class SchoolMemberTests
    {
        private static readonly Func<SchoolMember, bool> Named = new NamedSchoolMemberSpecification().Expression().Compile();

        [Fact]
        public void TeacherStudentAndParentGiveTheMatchingAccountRole()
        {
            Assert.Equal(nameof(Role.Teacher), SchoolMemberRole.Teacher.AccountRoleName());
            Assert.Equal(nameof(Role.Student), SchoolMemberRole.Student.AccountRoleName());
            Assert.Equal(nameof(Role.Parent), SchoolMemberRole.Parent.AccountRoleName());
        }

        [Fact]
        public void StaffAndAlumniGiveNoAccountRole()
        {
            Assert.Null(SchoolMemberRole.Staff.AccountRoleName());
            Assert.Null(SchoolMemberRole.Alumni.AccountRoleName());
        }

        [Fact]
        public void OnlyTeachersAndStaffCanBeNamed()
        {
            Assert.True(SchoolMemberRole.Teacher.CanShowName);
            Assert.True(SchoolMemberRole.Staff.CanShowName);
            Assert.False(SchoolMemberRole.Student.CanShowName);
            Assert.False(SchoolMemberRole.Parent.CanShowName);
            Assert.False(SchoolMemberRole.Alumni.CanShowName);
        }

        [Fact]
        public void PublicNamedTeacherWhoAgreedIsNamed() =>
            Assert.True(Named(Member(SchoolMemberRole.Teacher, showName: true, ProfileVisibility.Public, "Jane")));

        [Fact]
        public void PublicNamedStaffWhoAgreedIsNamed() =>
            Assert.True(Named(Member(SchoolMemberRole.Staff, showName: true, ProfileVisibility.Public, "Jane")));

        [Fact]
        public void TeacherWhoDidNotAgreeIsNotNamed() =>
            Assert.False(Named(Member(SchoolMemberRole.Teacher, showName: false, ProfileVisibility.Public, "Jane")));

        [Fact]
        public void PrivateTeacherIsNotNamed() =>
            Assert.False(Named(Member(SchoolMemberRole.Teacher, showName: true, ProfileVisibility.Private, "Jane")));

        [Fact]
        public void TeacherWithNoNameIsNotNamed() =>
            Assert.False(Named(Member(SchoolMemberRole.Teacher, showName: true, ProfileVisibility.Public, " ")));

        [Fact]
        public void StudentsParentsAndAlumniAreNeverNamed()
        {
            foreach (var role in new[] { SchoolMemberRole.Student, SchoolMemberRole.Parent, SchoolMemberRole.Alumni })
            {
                Assert.False(Named(Member(role, showName: true, ProfileVisibility.Public, "Sam")));
            }
        }

        private static SchoolMember Member(SchoolMemberRole role, bool showName, ProfileVisibility visibility, string? firstName) => new()
        {
            Role = role,
            ShowName = showName,
            User = new ApplicationUser { FirstName = firstName, ProfileVisibility = visibility },
        };
    }
}
