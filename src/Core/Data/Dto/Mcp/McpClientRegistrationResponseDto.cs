namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>The registered client (RFC 7591). Nothing is stored: the client id carries the registration, protected with
    /// Data Protection, and the secret is derived from it.</summary>
    public sealed class McpClientRegistrationResponseDto
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("client_id_issued_at")]
        public long ClientIdIssuedAt { get; set; }

        [JsonPropertyName("client_secret")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClientSecret { get; set; }

        /// <summary>0: the secret does not expire.</summary>
        [JsonPropertyName("client_secret_expires_at")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? ClientSecretExpiresAt { get; set; }

        [JsonPropertyName("redirect_uris")]
        public IReadOnlyList<string>? RedirectUris { get; set; }

        [JsonPropertyName("client_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClientName { get; set; }

        [JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }

        [JsonPropertyName("grant_types")]
        public IReadOnlyList<string>? GrantTypes { get; set; }

        [JsonPropertyName("response_types")]
        public IReadOnlyList<string>? ResponseTypes { get; set; }
    }
}
