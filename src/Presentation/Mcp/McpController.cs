namespace GamaEdtech.Presentation.Mcp
{
    using System.Diagnostics.CodeAnalysis;
    using System.Net.Http.Headers;
    using System.Text;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.Mcp;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The HTTP side of the MCP connector (the MCP endpoint itself is <c>/mcp</c>, see <see cref="ExamImportTools"/>):
    /// the OAuth 2.1 authorization server MCP clients sign in through, and the figure upload link. These are protocol endpoints at the paths OAuth clients expect, so they answer in OAuth's
    /// own JSON (or HTML pages) and real HTTP status codes, not the API's <c>ApiResponse</c> envelope.
    /// </summary>
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("")]
    public class McpController(Lazy<ILogger<McpController>> logger, Lazy<IMcpAuthorizationService> authorizationService, Lazy<IExamImportService> examImportService)
        : Common.Core.ControllerBase<McpController>(logger)
    {
        private const int MaxFigureUploadBytes = 6 * 1024 * 1024;

        [HttpGet(".well-known/oauth-authorization-server")]
        public IActionResult AuthorizationServerMetadata() => Json(authorizationService.Value.GetMetadata());

        /// <summary>Dynamic client registration (RFC 7591).</summary>
        [HttpPost("oauth/register")]
        public IActionResult Register([FromBody] McpClientRegistrationRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return OAuthError([new() { Reference = "invalid_client_metadata", Message = "The registration request is not valid JSON." }]);
            }

            var result = authorizationService.Value.RegisterClient(request);
            return result.OperationResult is OperationResult.Succeeded ? StatusCode(StatusCodes.Status201Created, result.Data) : OAuthError(result.Errors);
        }

        /// <summary>Starts the sign-in: checks the authorization request and shows the Gamatrain sign-in page.</summary>
        [HttpGet("oauth/authorize")]
        public IActionResult Authorize(
            [FromQuery(Name = "response_type")] string? responseType,
            [FromQuery(Name = "client_id")] string? clientId,
            [FromQuery(Name = "redirect_uri")] string? redirect,
            [FromQuery(Name = "state")] string? state,
            [FromQuery(Name = "code_challenge")] string? codeChallenge,
            [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
            [FromQuery(Name = "scope")] string? scope,
            [FromQuery(Name = "resource")] string? resource)
        {
            var result = authorizationService.Value.ValidateAuthorizationRequest(new()
            {
                ResponseType = responseType,
                ClientId = clientId,
                RedirectUri = redirect,
                State = state,
                CodeChallenge = codeChallenge,
                CodeChallengeMethod = codeChallengeMethod,
                Scope = scope,
                Resource = resource,
            });
            if (result.OperationResult is OperationResult.Succeeded)
            {
                return Page(McpPages.SignIn(result.Data!.Request, result.Data, null, null, false));
            }

            var error = result.Errors?.FirstOrDefault() ?? default;
            return error is { Info: "redirect", Value: string errorRedirect } ? Redirect(errorRedirect) : Page(McpPages.Notice(error.Message ?? "The sign-in request is invalid."), StatusCodes.Status400BadRequest);
        }

        /// <summary>The sign-in form: signs in to gama-api and sends the browser back to the client with a code.</summary>
        [HttpPost("oauth/authorize")]
        public async Task<IActionResult> SignIn([FromForm] string? request, [FromForm] string? identity, [FromForm] string? password, [FromForm] int? code)
        {
            if (!ModelState.IsValid)
            {
                return Page(McpPages.SignIn(request, null, identity, "The one-time code must be a number.", true));
            }

            var result = await authorizationService.Value.SignInAsync(new() { Request = request, Identity = identity, Password = password, Code = code });
            if (result is { OperationResult: OperationResult.Succeeded, Data.RedirectUri: { } redirectUri })
            {
                return Redirect(redirectUri.AbsoluteUri);
            }

            var error = result.Errors?.FirstOrDefault() ?? default;
            return error.Reference == "expired"
                ? Page(McpPages.Notice(error.Message ?? string.Empty), StatusCodes.Status400BadRequest)
                : Page(McpPages.SignIn(request, result.Data?.AuthorizationRequest, identity, error.Message, result.Data?.CodeRequired == true || code is not null));
        }

        /// <summary>Exchanges an authorization code for an access token (PKCE).</summary>
        [HttpPost("oauth/token")]
        public async Task<IActionResult> Token(
            [FromForm(Name = "grant_type")] string? grantType,
            [FromForm(Name = "code")] string? code,
            [FromForm(Name = "redirect_uri")] string? redirect,
            [FromForm(Name = "client_id")] string? clientId,
            [FromForm(Name = "client_secret")] string? clientSecret,
            [FromForm(Name = "code_verifier")] string? codeVerifier,
            [FromForm(Name = "resource")] string? resource)
        {
            // client_secret_basic: both parts are form-encoded before base64 (RFC 6749 2.3.1).
            if (AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) && header is { Scheme: "Basic", Parameter: { } basic })
            {
                var bytes = new byte[basic.Length];
                var credentials = Convert.TryFromBase64String(basic, bytes, out var length) ? Encoding.UTF8.GetString(bytes, 0, length).Split(':', 2) : [];
                clientId = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0].Replace('+', ' ')) : clientId;
                clientSecret = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1].Replace('+', ' ')) : clientSecret;
            }

            var result = await authorizationService.Value.ExchangeTokenAsync(new()
            {
                GrantType = grantType,
                Code = code,
                RedirectUri = redirect,
                ClientId = clientId,
                ClientSecret = clientSecret,
                CodeVerifier = codeVerifier,
                Resource = resource,
            });
            Response.Headers.CacheControl = "no-store";
            return result.OperationResult is OperationResult.Succeeded ? Json(result.Data) : OAuthError(result.Errors);
        }

        /// <summary>Uploads a figure image (multipart field <c>file</c>) to gama-api through a signed link from the get_figure_upload_link tool.</summary>
        [HttpPost("mcp/figures/{link}")]
        [RequestSizeLimit(MaxFigureUploadBytes)]
        public async Task<IActionResult> UploadFigure([NotNull] string link, IFormFile? file)
        {
            if (!ModelState.IsValid || file is null)
            {
                return BadRequest(new { ok = false, message = "Send the image as the multipart field file." });
            }

            using MemoryStream content = new();
            await file.CopyToAsync(content);
            var result = await examImportService.Value.AddFigureByLinkAsync(link, new() { Content = content.ToArray(), Name = file.FileName });
            return result.OperationResult is OperationResult.Succeeded
                ? Json(new { ok = true, figure = result.Data!.Figure, name = result.Data.Name, size = result.Data.Size })
                : BadRequest(new { ok = false, message = result.Errors?.FirstOrDefault().Message, code = result.Errors?.FirstOrDefault().Reference });
        }

        private ContentResult Page(string html, int statusCode = StatusCodes.Status200OK)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.XFrameOptions = "DENY";
            return new() { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = statusCode };
        }

        /// <summary>An OAuth error response: <c>{"error", "error_description"}</c>, 401 for a bad client, otherwise 400.</summary>
        private static JsonResult OAuthError(IEnumerable<Error>? errors)
        {
            var error = errors?.FirstOrDefault() ?? default;
            return new(new Dictionary<string, string?> { ["error"] = error.Reference ?? "invalid_request", ["error_description"] = error.Message })
            {
                StatusCode = error.Reference switch
                {
                    "invalid_client" => StatusCodes.Status401Unauthorized,
                    "server_error" => StatusCodes.Status500InternalServerError,
                    _ => StatusCodes.Status400BadRequest,
                },
            };
        }
    }
}
