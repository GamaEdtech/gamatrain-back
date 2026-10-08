namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>The issued access token (RFC 6749). No refresh token: the access token lives as long as the gama-api
    /// sign-in inside it, which cannot be renewed without the password.</summary>
    public sealed class McpTokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Scope { get; set; }
    }
}
