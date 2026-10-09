namespace GamaEdtech.Application.Service
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// The public origin MCP clients reach this API at (the OAuth issuer, the <c>/mcp</c> resource and the signed links):
    /// <c>Mcp:PublicUrl</c>, or the current request's origin when it is unset (local development). Behind the reverse
    /// proxy the request's scheme is <c>http</c>, so it must be set in every deployed environment.
    /// </summary>
    internal static class McpPublicUrl
    {
        public static string Get(IConfiguration configuration, HttpContext? httpContext)
        {
            var configured = configuration.GetValue<string?>("Mcp:PublicUrl");
            return !string.IsNullOrWhiteSpace(configured)
                ? configured.TrimEnd('/')
                : $"{httpContext?.Request.Scheme}://{httpContext?.Request.Host}{httpContext?.Request.PathBase}".TrimEnd('/');
        }
    }
}
