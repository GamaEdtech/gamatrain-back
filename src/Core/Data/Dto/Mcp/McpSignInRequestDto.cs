namespace GamaEdtech.Data.Dto.Mcp
{
    /// <summary>The sign-in form posted to <c>POST oauth/authorize</c>. The password goes straight to gama-api's login and
    /// is never stored.</summary>
    public sealed class McpSignInRequestDto
    {
        /// <summary><see cref="McpAuthorizationRequestDto.Request"/>.</summary>
        public string? Request { get; set; }

        public string? Identity { get; set; }

        public string? Password { get; set; }

        /// <summary>The one-time code gama-api sent when it asks for one (weak passwords).</summary>
        public int? Code { get; set; }
    }
}
