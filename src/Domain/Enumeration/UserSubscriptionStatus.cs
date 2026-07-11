namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class UserSubscriptionStatus : Enumeration<UserSubscriptionStatus, byte>
    {
        [Display]
        public static readonly UserSubscriptionStatus Pending = new(nameof(Pending), 0);

        [Display]
        public static readonly UserSubscriptionStatus Active = new(nameof(Active), 1);

        [Display]
        public static readonly UserSubscriptionStatus Expired = new(nameof(Expired), 2);

        [Display]
        public static readonly UserSubscriptionStatus Cancelled = new(nameof(Cancelled), 3);

        public UserSubscriptionStatus()
        {
        }

        public UserSubscriptionStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
