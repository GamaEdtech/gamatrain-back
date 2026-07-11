namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class LegacyGoogleAuthRequestViewModel
    {
        [Display]
        [Required]
        public string? IdToken { get; set; }
    }
}
