namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SpendSource : Enumeration<SpendSource, byte>
    {
        [Display]
        public static readonly SpendSource SubscriptionQuota = new(nameof(SubscriptionQuota), 0);

        [Display]
        public static readonly SpendSource Points = new(nameof(Points), 1);

        /// <summary>Paid on gama-api, before the content moved here; copied with gama-api's purchase history. Never refunded
        /// through this backend (the money was taken by gama-api).</summary>
        [Display]
        public static readonly SpendSource GamaApi = new(nameof(GamaApi), 2);

        public SpendSource()
        {
        }

        public SpendSource(string name, byte value) : base(name, value)
        {
        }
    }
}
