namespace GamaEdtech.Data.Dto.School
{
    public sealed class ManageSchoolContributionRequestDto
    {
        public long? Id { get; set; }
        public int UserId { get; set; }
        public long SchoolId { get; set; }
        public required SchoolContributionDto Data { get; set; }
    }
}
