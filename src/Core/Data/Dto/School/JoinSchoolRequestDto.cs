namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class JoinSchoolRequestDto
    {
        public required long UserId { get; set; }
        public required long SchoolId { get; set; }
        public required SchoolMemberRole Role { get; set; }

        /// <summary>Name the user on the school page (teachers and staff only). Also makes their profile Public.</summary>
        public bool ShowName { get; set; }
    }
}
