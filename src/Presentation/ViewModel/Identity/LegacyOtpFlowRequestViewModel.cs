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
    }
}
