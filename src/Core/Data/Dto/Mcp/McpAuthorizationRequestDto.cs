namespace GamaEdtech.Data.Dto.Mcp
{
    /// <summary>
    /// An OAuth authorization request (<c>GET oauth/authorize</c>, authorization code with PKCE). Once validated it
    /// travels through the sign-in form protected and time-limited (<see cref="Request"/>), so nothing is stored
    /// between showing the form and posting it.
    /// </summary>
    public sealed class McpAuthorizationRequestDto
    {
        public string? ResponseType { get; set; }

        public string? ClientId { get; set; }

        /// <summary>The name the client registered with (its own claim), shown on the sign-in page with the redirect host.</summary>
        public string? ClientName { get; set; }

        public string? RedirectUri { get; set; }

        public string? State { get; set; }

        public string? CodeChallenge { get; set; }

        public string? CodeChallengeMethod { get; set; }

        public string? Scope { get; set; }

        /// <summary>RFC 8707 resource indicator: must be this server's MCP endpoint when given.</summary>
        public string? Resource { get; set; }

        /// <summary>The validated request, protected and time-limited, for the sign-in form to post back.</summary>
        public string? Request { get; set; }
    }
}
