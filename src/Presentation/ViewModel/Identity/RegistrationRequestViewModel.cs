namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RegistrationRequestViewModel
    {
        [Display]
        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Display]
        [Required]
        public string? Password { get; set; }

        [Display]
        [Required]
        [Compare(nameof(Password))]
        public string? ConfirmPassword { get; set; }

        [Display]
        public string? FirstName { get; set; }

        [Display]
        public string? LastName { get; set; }

        /// <summary>Optional teacher invite code from an invite link (?invite=CODE). Used only when this request creates the account.</summary>
        [Display]
        [StringLength(10)]
        public string? ReferralCode { get; set; }
    }
}
