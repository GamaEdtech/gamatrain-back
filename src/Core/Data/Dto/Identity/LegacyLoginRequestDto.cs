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

        /// <summary>
        /// The end user's real IP, forwarded to gama-api as <c>TRUSTED_FORWARDED_IP</c> so its login
        /// rate-limiting/fraud checks see the actual client rather than this server's own IP (this app
        /// proxies the request, so gama-api would otherwise only ever see the proxy). Populated by
        /// <c>IdentityService</c> from the inbound request, not by the caller of this DTO.
        /// </summary>
        public string? ClientIpAddress { get; set; }
    }
}
