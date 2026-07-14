namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class QuotaFailureReason : Enumeration<QuotaFailureReason, byte>
    {
        [Display]
        public static readonly QuotaFailureReason NoActiveSubscription = new(nameof(NoActiveSubscription), 0);

        [Display]
        public static readonly QuotaFailureReason FeatureNotInPlan = new(nameof(FeatureNotInPlan), 1);

        [Display]
        public static readonly QuotaFailureReason QuotaExhausted = new(nameof(QuotaExhausted), 2);

        public QuotaFailureReason()
        {
        }

        public QuotaFailureReason(string name, byte value) : base(name, value)
        {
        }
    }
}
