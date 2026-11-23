namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The admin list of school memberships (what users said they are at a school), all filters optional.</summary>
    public sealed class SchoolMemberAdminListRequestDto
    {
        public SchoolMemberRole? Role { get; set; }

        public long? SchoolId { get; set; }

        public long? UserId { get; set; }

        /// <summary>The member's name, user name or email, or the school's name.</summary>
        public string? Text { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class SchoolMemberAdminListDto
    {
        public int Total { get; set; }

        /// <summary>How many memberships each role has, of all matching ones (whatever the role filter).</summary>
        public IReadOnlyList<SchoolMemberCountDto> RoleCounts { get; set; } = [];

        /// <summary>Distinct users and schools of all matching memberships.</summary>
        public int Users { get; set; }

        public int Schools { get; set; }

        public IReadOnlyList<SchoolMemberAdminItemDto> Items { get; set; } = [];
    }

    public sealed class SchoolMemberAdminItemDto
    {
        public long Id { get; set; }

        public long UserId { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public string? UserName { get; set; }

        public string? Email { get; set; }

        public long SchoolId { get; set; }

        public string? SchoolName { get; set; }

        public required SchoolMemberRole Role { get; set; }

        /// <summary>Agreed to be named on the school page (teachers and staff).</summary>
        public bool ShowName { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
