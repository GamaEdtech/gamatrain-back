namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyLoginRequestDto
    {
        public required string Identity { get; set; }
        public string? Password { get; set; }

        /// <summary>
        /// Set to "confirm" together with <see cref="Code"/> to complete a loginByOTP step-up (see
        /// CoreLoginResponse.Type). Omit on the initial login attempt.
        /// </summary>
        public string? Type { get; set; }
        public int? Code { get; set; }
    }
}
