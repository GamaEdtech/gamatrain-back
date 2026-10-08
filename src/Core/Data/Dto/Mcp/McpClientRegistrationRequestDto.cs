namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>An MCP client's dynamic client registration request (RFC 7591), e.g. ChatGPT registering its connector.</summary>
    public sealed class McpClientRegistrationRequestDto
    {
        [JsonPropertyName("redirect_uris")]
        public IReadOnlyList<string>? RedirectUris { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        /// <summary><c>none</c> (a public client using PKCE only), <c>client_secret_post</c> or <c>client_secret_basic</c>.</summary>
        [JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }

        [JsonPropertyName("grant_types")]
        public IReadOnlyList<string>? GrantTypes { get; set; }

        [JsonPropertyName("response_types")]
        public IReadOnlyList<string>? ResponseTypes { get; set; }
    }
}
