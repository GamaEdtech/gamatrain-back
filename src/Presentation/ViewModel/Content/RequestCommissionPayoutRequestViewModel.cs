namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class RequestCommissionPayoutRequestViewModel
    {
        /// <summary>Optional - omit to request the whole available balance. Whole cents, at least the payout threshold.</summary>
        [Display]
        public decimal? AmountUsd { get; set; }

        /// <summary>Optional, default StripeConnect (pays to the user's Stripe account; payout-account must be enabled). Manual needs destination.</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<PayoutMethod, byte>))]
        public PayoutMethod? Method { get; set; }

        /// <summary>Manual only: where to send the money (bank account/IBAN, PayPal email...).</summary>
        [Display]
        [StringLength(500)]
        public string? Destination { get; set; }
    }
}
