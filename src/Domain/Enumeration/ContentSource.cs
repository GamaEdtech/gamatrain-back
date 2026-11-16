namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Which external system a downloadable content item (and its owner-commission accrual) comes
    /// from. Only one member exists today (gama-api); kept as a smart enum, not a bool/hardcoded
    /// value, so a second content source can be added later without a schema change - mirrors
    /// Payment.Gateway/SubscriptionPlanGatewayMapping.Gateway.
    /// </summary>
    public sealed class ContentSource : Enumeration<ContentSource, byte>
    {
        [Display]
        public static readonly ContentSource GamaApiLegacy = new(nameof(GamaApiLegacy), 0);

        /// <summary>This backend's own copy (past papers in <c>PastPapers</c>, files in the private bucket), since 2026-11-16.</summary>
        [Display]
        public static readonly ContentSource Native = new(nameof(Native), 1);

        public ContentSource()
        {
        }

        public ContentSource(string name, byte value) : base(name, value)
        {
        }
    }
}
