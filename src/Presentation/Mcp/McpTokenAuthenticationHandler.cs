namespace GamaEdtech.Presentation.Mcp
{
    using System.Security.Claims;
    using System.Text.Encodings.Web;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Authenticates <c>/mcp</c> with an MCP access token (see <see cref="IMcpAuthorizationService"/>): the caller's usual
    /// claims, plus the gama-api token it carries (<see cref="GamaTokenClaim"/>) for the tools to forward. Only <c>/mcp</c>
    /// uses this scheme, so an MCP access token is worth nothing on the rest of the API. The challenge is forwarded to the
    /// MCP SDK's scheme, which answers 401 with the protected resource metadata the client starts its sign-in from.
    /// </summary>
    public sealed class McpTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "McpToken";

        /// <summary>The caller's gama-api token, decrypted from the access token; it exists only for this request.</summary>
        public const string GamaTokenClaim = "mcp_gama_token";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = TokenAuthenticationHandler.GetTokenFromHeader(Context.Request);
            if (string.IsNullOrEmpty(token))
            {
                return AuthenticateResult.NoResult();
            }

            var verified = await Context.RequestServices.GetRequiredService<IMcpAuthorizationService>().VerifyAccessTokenAsync(token);
            if (verified is not { Claims: { } claims, GamaToken: { } gamaToken })
            {
                return AuthenticateResult.Fail("The access token is invalid or expired.");
            }

            ClaimsIdentity identity = new([.. claims, new Claim(GamaTokenClaim, gamaToken)], Scheme.Name);
            return AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}
