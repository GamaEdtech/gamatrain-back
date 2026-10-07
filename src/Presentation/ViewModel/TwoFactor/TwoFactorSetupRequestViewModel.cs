namespace GamaEdtech.Presentation.ViewModel.TwoFactor
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class TwoFactorSetupRequestViewModel
    {
        /// <summary>The 6-digit code emailed by setup/email-code.</summary>
        [Display]
        [Required]
        [StringLength(10)]
        public string? EmailCode { get; set; }
    }
}
