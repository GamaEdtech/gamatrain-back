namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyLoginRequestDto
    {
        public required string Identity { get; set; }
        public string? Password { get; set; }
    }
}
