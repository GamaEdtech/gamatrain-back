namespace GamaEdtech.Data.Dto.Identity
{
    using Microsoft.AspNetCore.Http;

    public sealed class ManageAvatarRequestDto
    {
        public required long UserId { get; set; }
        public required IFormFile? Avatar { get; set; }
    }
}
