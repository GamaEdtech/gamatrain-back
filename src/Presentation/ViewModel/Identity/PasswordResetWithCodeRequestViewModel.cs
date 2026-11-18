namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Step 3 of a forgotten password: the emailed code and the new password.</summary>
    public sealed class PasswordResetWithCodeRequestViewModel
    {
        [Display]
        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Display]
        [Required]
        public string? Code { get; set; }

        [Display]
        [Required]
        public string? Password { get; set; }

        [Display]
        [Required]
        [Compare(nameof(Password))]
        public string? ConfirmPassword { get; set; }
    }
}
