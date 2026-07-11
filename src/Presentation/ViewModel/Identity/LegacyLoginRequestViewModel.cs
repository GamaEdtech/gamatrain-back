namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class LegacyLoginRequestViewModel
    {
        [Display]
        [Required]
        public string? Identity { get; set; }

        [Display]
        [Required]
        public string? Password { get; set; }
    }
}
