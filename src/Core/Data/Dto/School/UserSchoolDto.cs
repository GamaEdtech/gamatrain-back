namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>One school the user belongs to, in one role.</summary>
    public sealed class UserSchoolDto
    {
        public long SchoolId { get; set; }
        public string? SchoolName { get; set; }
        public required SchoolMemberRole Role { get; set; }
        public bool ShowName { get; set; }
        public DateTimeOffset CreationDate { get; set; }
    }
}
