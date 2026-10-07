namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class MarkPayoutPaidRequestViewModel : PayoutDecisionRequestViewModel
    {
        /// <summary>The bank/PayPal transaction id of the transfer.</summary>
        [Display]
        [Required]
        [StringLength(200)]
        public string? TransferReference { get; set; }
    }
}
