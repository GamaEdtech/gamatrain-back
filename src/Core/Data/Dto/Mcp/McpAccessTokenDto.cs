namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Security.Claims;

    /// <summary>A verified MCP access token: the caller's claims (as for any signed-in request) and the bearer token it carries
    /// (this backend's own <c>{userId}|{token}</c>), which the MCP tools pass on to say who is calling.</summary>
    public sealed class McpAccessTokenDto
    {
        public IReadOnlyList<Claim>? Claims { get; set; }

        public string? UserToken { get; set; }
    }
}
