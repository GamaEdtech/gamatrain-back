namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class AuthenticationResponseDto
    {
        public ApplicationUserDto? User { get; set; }

        /// <summary>True when this sign-in created the account (Google sign-up).</summary>
        public bool IsNewUser { get; set; }
    }
}
