namespace GamaEdtech.Presentation.ViewModel.TwoFactor
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class TwoFactorCodeRequestViewModel
    {
        /// <summary>The current 6-digit code from the caller's authenticator app.</summary>
        [Display]
        [Required]
        [StringLength(10)]
        public string? Code { get; set; }
    }
}
