namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>One quota bucket's allowance at one purchased billing interval - e.g. a plan can grant 50 for Monthly and 600 for Annual of the same feature/group.</summary>
    public sealed class PlanFeatureLimitViewModel
    {
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<BillingInterval, byte>))]
        public BillingInterval? BillingInterval { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        [Display]
        public int? Limit { get; set; }
    }
}
