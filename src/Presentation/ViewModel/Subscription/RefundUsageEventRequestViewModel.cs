namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Body of <c>POST admin/subscriptions/usage/{id}/refund</c>.</summary>
    public sealed class RefundUsageEventRequestViewModel
    {
        /// <summary>The admin's current 6-digit authenticator code (admin/twofactor).</summary>
        [Display]
        [Required]
        [StringLength(10)]
        public string? TwoFactorCode { get; set; }
    }
}
