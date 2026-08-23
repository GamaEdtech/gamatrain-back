namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyUpdateGroupRequestDto
    {
        public required string Token { get; set; }
        public required int Group { get; set; }
    }
}
