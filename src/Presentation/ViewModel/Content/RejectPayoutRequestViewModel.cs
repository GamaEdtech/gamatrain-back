namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RejectPayoutRequestViewModel : PayoutDecisionRequestViewModel
    {
        /// <summary>Shown to the owner.</summary>
        [Display]
        [Required]
        [StringLength(1000)]
        public string? Reason { get; set; }
    }
}
