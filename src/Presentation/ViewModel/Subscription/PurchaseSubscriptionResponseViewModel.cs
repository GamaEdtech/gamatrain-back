namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PurchaseSubscriptionResponseViewModel
    {
        public long UserSubscriptionId { get; set; }

        /// <summary>0 when <see cref="Switched"/> - an immediate switch bills via Stripe directly, not this app's own Payment row.</summary>
        public long PaymentId { get; set; }

        /// <summary>Set only for a fresh purchase (redirect here for Checkout) - null whenever <see cref="Switched"/> or <see cref="RequiresConfirmation"/>.</summary>
        public string? Url { get; set; }

        /// <summary>True when this call was applied as a switch on an already-existing subscription instead of starting a fresh purchase.</summary>
        public bool Switched { get; set; }

        /// <summary>True when nothing was applied yet and this response is a preview - resubmit the identical request with confirm:true to actually apply it and charge previewAmount.</summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>Set only alongside requiresConfirmation - the exact amount a confirm:true resubmit will charge right now.</summary>
        public decimal? PreviewAmount { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? PreviewCurrency { get; set; }
    }
}
