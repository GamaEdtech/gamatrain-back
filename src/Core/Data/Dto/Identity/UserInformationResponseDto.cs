namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserInformationResponseDto
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public AvatarDto? Avatar { get; set; }
        public GenderType? Gender { get; set; }
        public int? Grade { get; set; }
        public int? Group { get; set; }
        public long? CoreId { get; set; }

        public sealed class AvatarDto
        {
            public required byte[] Content { get; set; }
            public required string Name { get; set; }
        }
    }
}
