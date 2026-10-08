namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Mcp;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class McpAuthorizationService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<McpAuthorizationService>> localizer, Lazy<ILogger<McpAuthorizationService>> logger, Lazy<IConfiguration> configuration,
        Lazy<IDataProtectionProvider> dataProtectionProvider, Lazy<ICacheProvider> cacheProvider, Lazy<IIdentityService> identityService,
        Lazy<ITokenService> tokenService)
        : LocalizableServiceBase<McpAuthorizationService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IMcpAuthorizationService
    {
        private const string ClientPurpose = "GamaEdtech.Mcp.OAuthClient";
        private const string ClientSecretPurpose = "GamaEdtech.Mcp.OAuthClientSecret";
        private const string RequestPurpose = "GamaEdtech.Mcp.AuthorizationRequest";
        private const string CodePurpose = "GamaEdtech.Mcp.AuthorizationCode";
        private const string AccessTokenPurpose = "GamaEdtech.Mcp.AccessToken";
        private const string CodeCacheKeyPrefix = "McpAuthorizationCode_";
        private const string NoClientAuthentication = "none";

        /// <summary>Errors that can go back to the client's redirect URI rather than being shown on the sign-in page.</summary>
        private const string RedirectError = "redirect";

        private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

        /// <summary>gama-api groups that can't add questions: 2 member, 6 student.</summary>
        private static readonly int[] StudentGroups = [2, 6];

        private static readonly string[] AuthMethods = [NoClientAuthentication, "client_secret_post", "client_secret_basic"];

        public McpAuthorizationServerMetadataDto GetMetadata() => new()
        {
            Issuer = PublicUrl,
            AuthorizationEndpoint = $"{PublicUrl}/oauth/authorize",
            TokenEndpoint = $"{PublicUrl}/oauth/token",
            RegistrationEndpoint = $"{PublicUrl}/oauth/register",
        };

        public ResultData<McpClientRegistrationResponseDto> RegisterClient([NotNull] McpClientRegistrationRequestDto requestDto)
        {
            try
            {
                var redirectUris = requestDto.RedirectUris?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).ToList() ?? [];
                var authMethod = requestDto.TokenEndpointAuthMethod ?? "client_secret_basic";
                var invalid = requestDto switch
                {
                    _ when redirectUris.Count == 0 || !redirectUris.All(IsValidRedirectUri) =>
                        ("invalid_redirect_uri", "Every redirect URI must be an absolute https URL, or http on localhost."),
                    _ when !AuthMethods.Contains(authMethod, StringComparer.Ordinal) =>
                        ("invalid_client_metadata", $"token_endpoint_auth_method must be one of {string.Join(", ", AuthMethods)}."),
                    { GrantTypes: { } grantTypes } when !grantTypes.Contains("authorization_code", StringComparer.Ordinal) =>
                        ("invalid_client_metadata", "Only the authorization_code grant is supported."),
                    { ResponseTypes: { } responseTypes } when !responseTypes.Contains("code", StringComparer.Ordinal) =>
                        ("invalid_client_metadata", "Only the code response type is supported."),
                    _ => default((string Code, string Message)?),
                };
                if (invalid is { } error)
                {
                    return OAuthError<McpClientRegistrationResponseDto>(error.Code, error.Message);
                }

                // Nothing is stored: the client id carries the registration.
                var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var clientId = Protector(ClientPurpose).Protect(JsonSerializer.Serialize(new RegisteredClient(redirectUris, authMethod)));
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ClientId = clientId,
                        ClientIdIssuedAt = issuedAt,
                        ClientSecret = authMethod == NoClientAuthentication ? null : Protector(ClientSecretPurpose).Protect(clientId),
                        ClientSecretExpiresAt = authMethod == NoClientAuthentication ? null : 0,
                        RedirectUris = redirectUris,
                        ClientName = requestDto.ClientName,
                        TokenEndpointAuthMethod = authMethod,
                        GrantTypes = ["authorization_code"],
                        ResponseTypes = ["code"],
                    },
                };
            }
            catch (Exception exc)
            {
                return Failure<McpClientRegistrationResponseDto>(exc);
            }
        }

        public ResultData<McpAuthorizationRequestDto> ValidateAuthorizationRequest([NotNull] McpAuthorizationRequestDto requestDto)
        {
            try
            {
                var client = ReadClient(requestDto.ClientId);
                if (client is null)
                {
                    return OAuthError<McpAuthorizationRequestDto>("invalid_client", "This app connection is not known here. Remove the app and connect it again.");
                }

                var redirectUri = requestDto.RedirectUri ?? (client.RedirectUris.Count == 1 ? client.RedirectUris[0] : null);
                if (redirectUri is null || !client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
                {
                    return OAuthError<McpAuthorizationRequestDto>("invalid_request", "The redirect URI is not registered for this app.");
                }

                var invalid = requestDto switch
                {
                    { ResponseType: not "code" } => ("unsupported_response_type", "Only response_type=code is supported."),
                    { CodeChallenge: null or "" } => ("invalid_request", "code_challenge is required (PKCE)."),
                    { CodeChallengeMethod: not "S256" } => ("invalid_request", "code_challenge_method must be S256."),
                    { Resource: { } resource } when !IsMcpResource(resource) => ("invalid_target", "The resource must be this server's MCP endpoint."),
                    _ => default((string Code, string Message)?),
                };
                if (invalid is { } error)
                {
                    var redirect = QueryHelpers.AddQueryString(redirectUri, Query(("error", error.Code), ("error_description", error.Message), ("state", requestDto.State)));
                    return new(OperationResult.NotValid) { Errors = [new() { Message = error.Message, Reference = error.Code, Info = RedirectError, Value = redirect }] };
                }

                McpAuthorizationRequestDto request = new()
                {
                    ResponseType = requestDto.ResponseType,
                    ClientId = requestDto.ClientId,
                    RedirectUri = redirectUri,
                    State = requestDto.State,
                    CodeChallenge = requestDto.CodeChallenge,
                    CodeChallengeMethod = requestDto.CodeChallengeMethod,
                    Scope = requestDto.Scope,
                    Resource = requestDto.Resource,
                };
                request.Request = Protector(RequestPurpose).ToTimeLimitedDataProtector().Protect(JsonSerializer.Serialize(request), RequestLifetime);
                return new(OperationResult.Succeeded) { Data = request };
            }
            catch (Exception exc)
            {
                return Failure<McpAuthorizationRequestDto>(exc);
            }
        }

        public async Task<ResultData<McpSignInResultDto>> SignInAsync([NotNull] McpSignInRequestDto requestDto)
        {
            try
            {
                var request = Unprotect<McpAuthorizationRequestDto>(Protector(RequestPurpose).ToTimeLimitedDataProtector(), requestDto.Request);
                if (request is null)
                {
                    return OAuthError<McpSignInResultDto>("expired", "This sign-in page has expired. Go back to the app and connect Gamatrain again.");
                }

                if (string.IsNullOrWhiteSpace(requestDto.Identity) || string.IsNullOrEmpty(requestDto.Password))
                {
                    return OAuthError<McpSignInResultDto>("invalid_request", "Enter your email (or username) and password.");
                }

                // The same gama-api login as legacy-auth/login: it also links (or creates) the local user.
                var login = await identityService.Value.LegacyLoginAsync(new()
                {
                    Identity = requestDto.Identity.Trim(),
                    Password = requestDto.Password,
                    Type = requestDto.Code is null ? null : "confirm",
                    Code = requestDto.Code,
                });
                if (login.OperationResult is not OperationResult.Succeeded || login.Data is null)
                {
                    return OAuthError<McpSignInResultDto>("access_denied", login.Errors?.FirstOrDefault().Message ?? "Sign-in failed. Check your email and password.");
                }

                // A weak password: gama-api sent a one-time code instead of a token.
                if (login.Data.Token is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { CodeRequired = true } };
                }

                if (await identityService.Value.GetLegacyJwtGroupAsync(login.Data.Token) is { } group && StudentGroups.Contains(group))
                {
                    return OAuthError<McpSignInResultDto>("access_denied", "This is a student account. Only teacher accounts can add questions to Gamatrain. Sign in with a teacher account.");
                }

                var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                AuthorizationCode payload = new(request.ClientId!, request.RedirectUri!, request.CodeChallenge!, request.Scope, login.Data.Token,
                    login.Data.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(29));
                await cacheProvider.Value.SetAsync(CodeCacheKey(code), Protector(CodePurpose).Protect(JsonSerializer.Serialize(payload)),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CodeLifetime });

                return new(OperationResult.Succeeded)
                {
                    Data = new() { RedirectUri = new(QueryHelpers.AddQueryString(request.RedirectUri!, Query(("code", code), ("state", request.State)))) },
                };
            }
            catch (Exception exc)
            {
                return Failure<McpSignInResultDto>(exc);
            }
        }

        public async Task<ResultData<McpTokenResponseDto>> ExchangeTokenAsync([NotNull] McpTokenRequestDto requestDto)
        {
            try
            {
                if (requestDto.GrantType != "authorization_code")
                {
                    return OAuthError<McpTokenResponseDto>("unsupported_grant_type", "Only the authorization_code grant is supported.");
                }

                var client = ReadClient(requestDto.ClientId);
                if (client is null || (client.AuthMethod != NoClientAuthentication && !IsClientSecret(requestDto.ClientId!, requestDto.ClientSecret)))
                {
                    return OAuthError<McpTokenResponseDto>("invalid_client", "Unknown client or wrong client secret.");
                }

                // Single use: the code is removed before it is checked.
                var key = CodeCacheKey(requestDto.Code ?? string.Empty);
                var stored = await cacheProvider.Value.GetAsync<string>(key);
                await cacheProvider.Value.RemoveAsync(key);
                var code = Unprotect<AuthorizationCode>(Protector(CodePurpose), stored);
                var invalid = code switch
                {
                    null => ("invalid_grant", "The authorization code is invalid, expired or already used."),
                    _ when code.ClientId != requestDto.ClientId => ("invalid_grant", "The authorization code was issued to another client."),
                    _ when requestDto.RedirectUri is not null && requestDto.RedirectUri != code.RedirectUri => ("invalid_grant", "The redirect URI does not match the authorization request."),
                    _ when !IsCodeVerifier(requestDto.CodeVerifier, code.CodeChallenge) => ("invalid_grant", "The code verifier does not match (PKCE)."),
                    _ when requestDto.Resource is not null && !IsMcpResource(requestDto.Resource) => ("invalid_target", "The resource must be this server's MCP endpoint."),
                    _ when code.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1) => ("invalid_grant", "The Gamatrain sign-in has expired. Sign in again."),
                    _ => default((string Code, string Message)?),
                };
                if (invalid is { } error)
                {
                    return OAuthError<McpTokenResponseDto>(error.Code, error.Message);
                }

                // The access token lives as long as the gama-api sign-in inside it.
                var accessToken = Protector(AccessTokenPurpose).ToTimeLimitedDataProtector()
                    .Protect(JsonSerializer.Serialize(new AccessToken(code!.GamaToken, code.ClientId)), code.ExpiresAt);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        AccessToken = accessToken,
                        ExpiresIn = (long)(code.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
                        Scope = code.Scope,
                    },
                };
            }
            catch (Exception exc)
            {
                return Failure<McpTokenResponseDto>(exc);
            }
        }

        public async Task<McpAccessTokenDto?> VerifyAccessTokenAsync([NotNull] string accessToken)
        {
            try
            {
                var token = Unprotect<AccessToken>(Protector(AccessTokenPurpose).ToTimeLimitedDataProtector(), accessToken);
                if (token is null)
                {
                    return null;
                }

                // The gama-api token is checked like any other (signature, expiry, the linked local user is enabled).
                var verified = await tokenService.Value.VerifyLegacyTokenAsync(token.GamaToken);
                return verified?.Claims is null ? null : new() { Claims = [.. verified.Claims], GamaToken = token.GamaToken };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        private string PublicUrl => McpPublicUrl.Get(configuration.Value, HttpContextAccessor.Value.HttpContext);

        private bool IsMcpResource(string resource) => string.Equals(resource.TrimEnd('/'), $"{PublicUrl}/mcp", StringComparison.OrdinalIgnoreCase);

        private IDataProtector Protector(string purpose) => dataProtectionProvider.Value.CreateProtector(purpose);

        private RegisteredClient? ReadClient(string? clientId) => Unprotect<RegisteredClient>(Protector(ClientPurpose), clientId);

        /// <summary>The secret is the client id protected for <see cref="ClientSecretPurpose"/>, so nothing is stored.</summary>
        private bool IsClientSecret(string clientId, string? secret) => TryUnprotect(Protector(ClientSecretPurpose), secret) == clientId;

        private static string? TryUnprotect(IDataProtector protector, string? value)
        {
            try
            {
                return value is null ? null : protector.Unprotect(value);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static T? Unprotect<T>(IDataProtector protector, string? value)
            where T : class
        {
            var json = TryUnprotect(protector, value);
            try
            {
                return json is null ? null : JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsValidRedirectUri(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.IsNullOrEmpty(uri.Fragment)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

        private static bool IsCodeVerifier(string? verifier, string challenge) =>
            verifier is { Length: >= 43 and <= 128 }
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
                Encoding.ASCII.GetBytes(challenge));

        private static string CodeCacheKey(string code) => $"{CodeCacheKeyPrefix}{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)))}";

        private static IEnumerable<KeyValuePair<string, string?>> Query(params (string Name, string? Value)[] values) =>
            values.Where(t => !string.IsNullOrEmpty(t.Value)).Select(t => new KeyValuePair<string, string?>(t.Name, t.Value));

        private static ResultData<T> OAuthError<T>(string code, string message) =>
            new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = code }] };

        private ResultData<T> Failure<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, Reference = "server_error" }] };
        }

        /// <summary>A registered client, carried (protected) by its client id.</summary>
        private sealed record RegisteredClient(IReadOnlyList<string> RedirectUris, string AuthMethod);

        /// <summary>What an authorization code stands for, kept (protected) in the cache for <see cref="CodeLifetime"/>.</summary>
        private sealed record AuthorizationCode(string ClientId, string RedirectUri, string CodeChallenge, string? Scope, string GamaToken, DateTimeOffset ExpiresAt);

        /// <summary>The payload of an access token.</summary>
        private sealed record AccessToken(string GamaToken, string ClientId);
    }
}
