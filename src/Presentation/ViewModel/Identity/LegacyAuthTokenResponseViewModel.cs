namespace GamaEdtech.Presentation.ViewModel.Identity
{
    public sealed class LegacyAuthTokenResponseViewModel
    {
        /// <summary>
        /// Set (e.g. "loginByOTP") when another step is required instead of a token - Token/ExpirationTime are
        /// unset in that case. Resubmit login with Type="confirm" and the received code to complete it.
        /// </summary>
        public string? Type { get; set; }

        public string? Token { get; set; }

        public DateTimeOffset? ExpirationTime { get; set; }
    }
}
