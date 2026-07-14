namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json.Serialization;

    public sealed class CoreGoogleAuthResponse
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("info")]
        public CoreAuthUserInfoResponse? Info { get; set; }

        [JsonPropertyName("jwtToken")]
        public string? JwtToken { get; set; }
    }
}
