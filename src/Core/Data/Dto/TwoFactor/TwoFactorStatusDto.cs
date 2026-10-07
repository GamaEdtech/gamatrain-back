namespace GamaEdtech.Data.Dto.TwoFactor
{
    public sealed class TwoFactorStatusDto
    {
        /// <summary>True once an authenticator app has been set up and confirmed with a code.</summary>
        public bool Enabled { get; set; }
    }
}
