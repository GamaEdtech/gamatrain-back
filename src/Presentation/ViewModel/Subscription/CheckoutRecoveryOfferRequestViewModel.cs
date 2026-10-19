namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class CheckoutRecoveryOfferRequestViewModel
    {
        /// <summary>The token from the checkout recovery email's link.</summary>
        [Display]
        [Required]
        [StringLength(1000)]
        public string? Token { get; set; }
    }
}
