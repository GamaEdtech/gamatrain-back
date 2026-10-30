namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Body of <c>POST admin/subscriptions/plans/{id}/sync-features</c>.</summary>
    public sealed class SyncPlanFeaturesRequestViewModel
    {
        /// <summary>The admin's current 6-digit authenticator code (admin/twofactor).</summary>
        [Display]
        [Required]
        [StringLength(10)]
        public string? TwoFactorCode { get; set; }
    }
}
