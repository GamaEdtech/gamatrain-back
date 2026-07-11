namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class LegacyAuthResponseDto
    {
        public required string Token { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public GenderType? Gender { get; set; }
        public int? Group { get; set; }
        public AvatarDto? Avatar { get; set; }

        public sealed class AvatarDto
        {
            public required byte[] Content { get; set; }
            public required string Name { get; set; }
        }
    }
}
