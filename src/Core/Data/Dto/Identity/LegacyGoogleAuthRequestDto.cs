namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyGoogleAuthRequestDto
    {
        public required string IdToken { get; set; }
        /// <summary>Optional teacher invite code (?invite=CODE); used only when this request creates the account.</summary>
        public string? ReferralCode { get; set; }
    }
}
