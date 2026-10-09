namespace GamaEdtech.Data.Dto.Mcp
{
    /// <summary>The outcome of the sign-in form: where to send the browser back to (with the authorization code), or that
    /// gama-api wants a one-time code first.</summary>
    public sealed class McpSignInResultDto
    {
        /// <summary>The client's redirect URI with <c>code</c> and <c>state</c>.</summary>
        public Uri? RedirectUri { get; set; }

        /// <summary>gama-api sent a one-time code; show the form again with a code field.</summary>
        public bool CodeRequired { get; set; }

        /// <summary>The request being signed in for, to show the form again with the app asking (also on a failure).</summary>
        public McpAuthorizationRequestDto? AuthorizationRequest { get; set; }
    }
}
