namespace GamaEdtech.Presentation.ViewModel.TwoFactor
{
    public sealed class AuthenticatorSetupResponseViewModel
    {
        /// <summary>Base32 secret, for typing into the authenticator app by hand.</summary>
        public string? SharedKey { get; set; }

        /// <summary>otpauth:// URI - render it as a QR code to scan with Google Authenticator or any TOTP app.</summary>
        public string? AuthenticatorUri { get; set; }
    }
}
