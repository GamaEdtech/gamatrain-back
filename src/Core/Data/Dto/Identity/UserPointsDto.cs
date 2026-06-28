namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class UserPointsDto
    {
        public long UserId { get; set; }
        public string? Name { get; set; }
        public long Points { get; set; }
        public string? AvatarUri { get; set; }
    }
}
