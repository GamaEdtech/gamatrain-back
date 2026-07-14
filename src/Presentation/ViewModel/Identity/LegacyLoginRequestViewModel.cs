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

        /// <summary>
        /// Set to "confirm" together with <see cref="Code"/> to complete an OTP step-up requested via a prior
        /// response's Type = "loginByOTP". Omit on the initial attempt.
        /// </summary>
        [Display]
        public string? Type { get; set; }

        [Display]
        public int? Code { get; set; }
    }
}
