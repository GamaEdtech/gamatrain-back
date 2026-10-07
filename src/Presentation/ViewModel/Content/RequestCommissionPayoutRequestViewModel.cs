namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RequestCommissionPayoutRequestViewModel
    {
        /// <summary>Optional - omit to request the whole available balance. Whole cents, at least the payout threshold.</summary>
        [Display]
        public decimal? AmountUsd { get; set; }

        /// <summary>Where to send the money (bank account/IBAN, PayPal email...).</summary>
        [Display]
        [Required]
        [StringLength(500)]
        public string? Destination { get; set; }
    }
}
