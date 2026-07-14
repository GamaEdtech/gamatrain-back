namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyBridgeTokenResponseDto
    {
        /// <summary>
        /// Set (e.g. "loginByOTP") when gama-api requires another step instead of returning a token - in that
        /// case UserId/Token/ExpirationTime are all unset. Null on a normal successful auth.
        /// </summary>
        public string? Type { get; set; }
        public long UserId { get; set; }
        public string? Token { get; set; }
        public DateTimeOffset? ExpirationTime { get; set; }
    }
}
