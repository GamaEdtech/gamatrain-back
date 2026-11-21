namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class PublicProfileRelatedTeachersRequestDto
    {
        public required string ProfileHandle { get; set; }
        public required long UserId { get; set; }
        public int Limit { get; set; }
    }

    public sealed class PublicProfileRelatedTeachersDto
    {
        public int TotalRecordsCount { get; set; }
        public IReadOnlyList<PublicProfileDto> Teachers { get; set; } = [];
    }
}
