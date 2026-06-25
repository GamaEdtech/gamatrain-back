namespace GamaEdtech.Data.Dto.Identity
{
    using Microsoft.AspNetCore.Http;

    public sealed class UpdateUserRequestDto
    {
        public required long Id { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required string PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public IFormFile? Avatar { get; set; }
    }
}
