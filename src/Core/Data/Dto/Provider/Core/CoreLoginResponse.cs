namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json.Serialization;

    public sealed class CoreLoginResponse
    {
        /// <summary>
        /// Only present when gama-api requires an OTP step-up instead of logging in directly (observed value:
        /// "loginByOTP", sent for weak/easy-to-guess passwords - undocumented in gama-api's own OpenAPI spec).
        /// Absent (null) on a normal successful login.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("info")]
        public CoreAuthUserInfoResponse? Info { get; set; }

        [JsonPropertyName("jwtToken")]
        public string? JwtToken { get; set; }
    }
}
