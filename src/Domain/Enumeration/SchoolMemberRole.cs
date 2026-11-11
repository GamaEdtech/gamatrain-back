namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// How a user is connected to one school (a SchoolMember row). Joining a school also gives the account the matching
    /// <see cref="Role"/> (see <see cref="AccountRoleName"/>); Staff and Alumni have no account role. The values are
    /// stored in SchoolMembers.Role - never renumber them.
    /// </summary>
    public sealed class SchoolMemberRole : Enumeration<SchoolMemberRole, byte>
    {
        [Display]
        public static readonly SchoolMemberRole Teacher = new(nameof(Teacher), 1);

        [Display]
        public static readonly SchoolMemberRole Staff = new(nameof(Staff), 2);

        [Display]
        public static readonly SchoolMemberRole Student = new(nameof(Student), 3);

        [Display]
        public static readonly SchoolMemberRole Parent = new(nameof(Parent), 4);

        [Display]
        public static readonly SchoolMemberRole Alumni = new(nameof(Alumni), 5);

        public SchoolMemberRole()
        {
        }

        public SchoolMemberRole(string name, byte value) : base(name, value)
        {
        }

        /// <summary>Teachers and staff can be shown by name on the school page; the other roles are only counted.</summary>
        public bool CanShowName => this == Teacher || this == Staff;

        /// <summary>The account role a member of this kind gets, or null when there is none (Staff, Alumni).</summary>
        public string? AccountRoleName() => Name switch
        {
            nameof(Teacher) => nameof(Role.Teacher),
            nameof(Student) => nameof(Role.Student),
            nameof(Parent) => nameof(Role.Parent),
            _ => null,
        };
    }
}
