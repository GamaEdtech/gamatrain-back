namespace GamaEdtech.Data.Dto.School
{
    public sealed class CreateSchoolIssuesContributionRequestDto
    {
        public long SchoolId { get; set; }
        public int CreationUserId { get; set; }
        public string? Description { get; set; }
    }
}
