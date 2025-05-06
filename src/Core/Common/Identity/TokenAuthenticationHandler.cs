namespace GamaEdtech.Common.Identity
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using System.Threading.Tasks;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using static GamaEdtech.Common.Core.Constants;

    public class TokenAuthenticationHandler(IOptionsMonitor<TokenAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<TokenAuthenticationSchemeOptions>(options, logger, encoder)
    {
        private const string BearerPrefix = "Bearer ";

        public static string? GetTokenFromHeader([NotNull] HttpRequest httpRequest)
        {
            var authorization = httpRequest.Headers.Authorization.ToString();
            return authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorization[BearerPrefix.Length..]
                : null;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = GetTokenFromHeader(Context.Request);
            if (token is null)
            {
                return AuthenticateResult.NoResult();
            }

            var data = token.Split(DelimiterAlternate, 2, StringSplitOptions.RemoveEmptyEntries);
            if (data.Length != 2)
            {
                return AuthenticateResult.NoResult();
            }

            var identityService = Context.RequestServices.GetRequiredService<ITokenService>();
            var result = await identityService.VerifyTokenAsync(new VerifyTokenRequest
            {
                Token = data[1],
                UserId = data[0],
                TokenProvider = PermissionConstants.ApiDataProtectorTokenProvider,
                Purpose = PermissionConstants.ApiDataProtectorTokenProviderAccessToken,
            });
            if (result?.Claims is null)
            {
                return AuthenticateResult.NoResult();
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(result.Claims, Scheme.Name));
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
