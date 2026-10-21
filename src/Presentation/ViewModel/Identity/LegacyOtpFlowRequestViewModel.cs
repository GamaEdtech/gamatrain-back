namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class LegacyOtpFlowRequestViewModel
    {
        [Display]
        [Required]
        public string? Type { get; set; }

        [Display]
        [Required]
        public string? Identity { get; set; }

        [Display]
        public int? Code { get; set; }

        [Display]
        public string? Password { get; set; }

        /// <summary>Optional teacher invite code (?invite=CODE), register only: kept for a day and applied at the login that creates the account.</summary>
        [Display]
        [StringLength(10)]
        public string? ReferralCode { get; set; }
    }
}
