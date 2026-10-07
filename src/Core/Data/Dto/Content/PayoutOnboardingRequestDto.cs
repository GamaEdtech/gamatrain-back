namespace GamaEdtech.Data.Dto.Content
{
    public sealed class PayoutOnboardingRequestDto
    {
        public long UserId { get; set; }

        /// <summary>ISO 3166-1 alpha-2; only used when the Stripe account doesn't exist yet (Stripe fixes it at creation).</summary>
        public string? Country { get; set; }

        /// <summary>Frontend page Stripe sends the owner back to; its origin must be one of CorsUrls.</summary>
        public string? ReturnUrl { get; set; }
    }
}
