namespace GamaEdtech.Data.Dto.Mcp
{
    using System.Security.Claims;

    /// <summary>A verified MCP access token: the caller's claims (as for any signed-in request) and the gama-api token it
    /// carries, which the MCP tools forward to gama-api.</summary>
    public sealed class McpAccessTokenDto
    {
        public IReadOnlyList<Claim>? Claims { get; set; }

        public string? GamaToken { get; set; }
    }
}
