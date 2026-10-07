namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Body of an admin approve decision; reject and mark-paid extend it.</summary>
    public class PayoutDecisionRequestViewModel
    {
        /// <summary>The admin's current 6-digit authenticator code (admin/twofactor).</summary>
        [Display]
        [Required]
        [StringLength(10)]
        public string? TwoFactorCode { get; set; }
    }
}
