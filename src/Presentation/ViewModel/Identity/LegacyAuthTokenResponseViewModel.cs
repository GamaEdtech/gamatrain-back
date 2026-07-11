namespace GamaEdtech.Presentation.ViewModel.Identity
{
    public sealed class LegacyAuthTokenResponseViewModel
    {
        public string? Token { get; set; }

        public DateTimeOffset? ExpirationTime { get; set; }
    }
}
