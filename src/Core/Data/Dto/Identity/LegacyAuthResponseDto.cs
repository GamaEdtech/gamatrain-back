namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class LegacyAuthResponseDto
    {
        /// <summary>
        /// Set (e.g. "loginByOTP") when gama-api requires another step instead of returning a token - in that
        /// case every other field including <see cref="Token"/> is null. Null on a normal successful auth.
        /// </summary>
        public string? Type { get; set; }
        public string? Token { get; set; }
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
