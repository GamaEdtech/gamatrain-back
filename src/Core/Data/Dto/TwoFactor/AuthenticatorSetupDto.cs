namespace GamaEdtech.Data.Dto.TwoFactor
{
    public sealed class AuthenticatorSetupDto
    {
        /// <summary>Base32 secret, for typing into the authenticator app by hand.</summary>
        public string? SharedKey { get; set; }

        /// <summary>otpauth:// URI - render it as a QR code for Google Authenticator and similar apps.</summary>
        public string? AuthenticatorUri { get; set; }
    }
}
