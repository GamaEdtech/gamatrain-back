namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Step 1 of signing up or resetting a password: the address the code is sent to.</summary>
    public sealed class EmailCodeRequestViewModel
    {
        [Display]
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
    }
}
