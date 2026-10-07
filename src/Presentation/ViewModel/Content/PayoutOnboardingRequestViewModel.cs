namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PayoutOnboardingRequestViewModel
    {
        /// <summary>ISO 3166-1 alpha-2 country of the user's bank account. Needed only the first time (Stripe fixes it).</summary>
        [Display]
        [StringLength(2)]
        public string? Country { get; set; }

        /// <summary>Frontend page to come back to after Stripe; must be on one of our own domains.</summary>
        [Display]
        [Required]
        [StringLength(500)]
        public string? ReturnUrl { get; set; }
    }
}
