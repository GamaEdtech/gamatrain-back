namespace GamaEdtech.Data.Dto.Connection
{
    public sealed class FollowRequestsResponseDto
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string? AvatarUri { get; set; }
        public string? Name { get; set; }
    }
}
