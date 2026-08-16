namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SwitchSubscriptionPlanResponseViewModel
    {
        public bool Success { get; set; }

        /// <summary>True if the switch applied right away (upgrade); false if it's deferred to EffectiveDate (downgrade).</summary>
        public bool Immediate { get; set; }

        public DateTimeOffset? EffectiveDate { get; set; }

        /// <summary>True when nothing was applied yet and this response is a preview - resubmit the identical request with confirm:true to actually apply it and charge previewAmount.</summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>Set only alongside requiresConfirmation - the exact amount a confirm:true resubmit will charge right now.</summary>
        public decimal? PreviewAmount { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Currency, byte>))]
        public Currency? PreviewCurrency { get; set; }
    }
}
