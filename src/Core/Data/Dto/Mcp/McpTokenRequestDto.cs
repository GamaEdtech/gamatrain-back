namespace GamaEdtech.Data.Dto.Mcp
{
    /// <summary>A token request (<c>POST oauth/token</c>, <c>authorization_code</c> grant with PKCE).</summary>
    public sealed class McpTokenRequestDto
    {
        public string? GrantType { get; set; }

        public string? Code { get; set; }

        public string? RedirectUri { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public string? CodeVerifier { get; set; }

        public string? Resource { get; set; }
    }
}
