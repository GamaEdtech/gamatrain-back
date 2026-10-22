namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class RegistrationRequestDto
    {
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        /// <summary>Optional teacher invite code (?invite=CODE); used only when this request creates the account.</summary>
        public string? ReferralCode { get; set; }
    }
}
