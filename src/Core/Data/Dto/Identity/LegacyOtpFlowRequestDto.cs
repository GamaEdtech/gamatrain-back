namespace GamaEdtech.Data.Dto.Identity
{
    /// <summary>
    /// Shared shape for gama-api's /users/register and /users/recovery multi-step (request/resend_code/confirm/final) flows.
    /// </summary>
    public sealed class LegacyOtpFlowRequestDto
    {
        public required string Type { get; set; }
        public required string Identity { get; set; }
        public int? Code { get; set; }
        public string? Password { get; set; }

        /// <summary>
        /// The end user's real IP, forwarded to gama-api as <c>TRUSTED_FORWARDED_IP</c> so its
        /// register/recovery rate-limiting/fraud checks see the actual client rather than this server's
        /// own IP (this app proxies the request, so gama-api would otherwise only ever see the proxy).
        /// Populated by <c>IdentityService</c> from the inbound request, not by the caller of this DTO.
        /// </summary>
        public string? ClientIpAddress { get; set; }
    }
}
