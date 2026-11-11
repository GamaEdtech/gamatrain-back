namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The people of a school: the members it may name, and how many members it has per role.</summary>
    public sealed class SchoolMembersDto
    {
        public IEnumerable<NamedSchoolMemberDto> NamedMembers { get; set; } = [];
        public IEnumerable<SchoolMemberCountDto> Counts { get; set; } = [];
    }

    public sealed class NamedSchoolMemberDto
    {
        public required SchoolMemberRole Role { get; set; }
        public string? FullName { get; set; }
        public string? AvatarUri { get; set; }
        public required string Handle { get; set; }
    }

    public sealed class SchoolMemberCountDto
    {
        public required SchoolMemberRole Role { get; set; }
        public int Count { get; set; }
    }
}
