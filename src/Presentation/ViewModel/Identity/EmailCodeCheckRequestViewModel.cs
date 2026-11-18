namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Step 2 of signing up or resetting a password: the emailed code, checked before the password is chosen.</summary>
    public sealed class EmailCodeCheckRequestViewModel
    {
        [Display]
        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Display]
        [Required]
        public string? Code { get; set; }
    }
}
