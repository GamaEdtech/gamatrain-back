namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Mcp;

    /// <summary>
    /// The OAuth 2.1 authorization server MCP clients (ChatGPT, Claude, Codex...) connect through, for the <c>/mcp</c>
    /// endpoint only: dynamic client registration, authorization code with PKCE, and a sign-in page that signs in to
    /// gama-api (the same login as <c>legacy-auth/login</c>). The access token wraps the user's gama-api token, protected
    /// with Data Protection: the client never sees the gama-api token, and the access token is accepted on <c>/mcp</c>
    /// only. Stateless except for single-use authorization codes and signed-out access tokens (in the cache). See
    /// docs/api/authentication.md, "MCP connector (OAuth)". Failures carry the OAuth error code in <c>Error.Reference</c>.
    /// </summary>
    [Injectable]
    public interface IMcpAuthorizationService
    {
        McpAuthorizationServerMetadataDto GetMetadata();

        ResultData<McpClientRegistrationResponseDto> RegisterClient([NotNull] McpClientRegistrationRequestDto requestDto);

        /// <summary>
        /// Validates an authorization request. Fails with <c>Error.Info</c> = <c>redirect</c> when the error can go back
        /// to the client's redirect URI (<c>Error.Value</c>), otherwise it must be shown to the user. On success
        /// <see cref="McpAuthorizationRequestDto.Request"/> carries the request for the sign-in form.
        /// </summary>
        ResultData<McpAuthorizationRequestDto> ValidateAuthorizationRequest([NotNull] McpAuthorizationRequestDto requestDto);

        /// <summary>Signs in to gama-api (teacher and staff accounts only) and issues an authorization code.</summary>
        Task<ResultData<McpSignInResultDto>> SignInAsync([NotNull] McpSignInRequestDto requestDto);

        Task<ResultData<McpTokenResponseDto>> ExchangeTokenAsync([NotNull] McpTokenRequestDto requestDto);

        /// <summary>The claims and gama-api token behind an MCP access token, or null when it isn't valid any more.</summary>
        Task<McpAccessTokenDto?> VerifyAccessTokenAsync([NotNull] string accessToken);

        /// <summary>Signs the user out of this connection: the access token is refused from now on (until it would have
        /// expired), so the client signs in again, and the gama-api session inside it ends. Fails with <c>logoutFailed</c>
        /// when gama-api didn't confirm the end of its session: the connection is signed out, the session may still be open.</summary>
        Task<ResultData<bool>> SignOutAsync([NotNull] string accessToken);
    }
}
