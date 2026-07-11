namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyBridgeTokenResponseDto
    {
        public long UserId { get; set; }
        public string? Token { get; set; }
        public DateTimeOffset? ExpirationTime { get; set; }
    }
}
