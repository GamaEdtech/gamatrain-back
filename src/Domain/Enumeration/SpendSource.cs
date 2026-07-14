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

        public SpendSource()
        {
        }

        public SpendSource(string name, byte value) : base(name, value)
        {
        }
    }
}
