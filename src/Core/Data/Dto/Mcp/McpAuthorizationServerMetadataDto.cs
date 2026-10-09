namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>OAuth authorization server metadata (RFC 8414), served at <c>/.well-known/oauth-authorization-server</c>.</summary>
    public sealed class McpAuthorizationServerMetadataDto
    {
        [JsonPropertyName("issuer")]
        public string? Issuer { get; set; }

        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }

        [JsonPropertyName("registration_endpoint")]
        public string? RegistrationEndpoint { get; set; }

        [JsonPropertyName("response_types_supported")]
        public IReadOnlyList<string> ResponseTypesSupported { get; set; } = ["code"];

        [JsonPropertyName("grant_types_supported")]
        public IReadOnlyList<string> GrantTypesSupported { get; set; } = ["authorization_code"];

        [JsonPropertyName("code_challenge_methods_supported")]
        public IReadOnlyList<string> CodeChallengeMethodsSupported { get; set; } = ["S256"];

        [JsonPropertyName("token_endpoint_auth_methods_supported")]
        public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; set; } = ["none", "client_secret_post", "client_secret_basic"];

        [JsonPropertyName("service_documentation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ServiceDocumentation { get; set; }
    }
}
