namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyGoogleAuthRequestDto
    {
        public required string IdToken { get; set; }

        /// <summary>
        /// The end user's real IP, forwarded to gama-api as <c>TRUSTED_FORWARDED_IP</c> so its login
        /// rate-limiting/fraud checks see the actual client rather than this server's own IP (this app
        /// proxies the request, so gama-api would otherwise only ever see the proxy). Populated by
        /// <c>IdentityService</c> from the inbound request, not by the caller of this DTO.
        /// </summary>
        public string? ClientIpAddress { get; set; }
    }
}
