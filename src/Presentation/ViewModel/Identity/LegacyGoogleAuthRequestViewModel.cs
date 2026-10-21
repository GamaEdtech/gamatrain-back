namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class LegacyGoogleAuthRequestViewModel
    {
        [Display]
        [Required]
        public string? IdToken { get; set; }

        /// <summary>Optional teacher invite code from an invite link (?invite=CODE). Used only when this request creates the account.</summary>
        [Display]
        [StringLength(10)]
        public string? ReferralCode { get; set; }
    }
}
