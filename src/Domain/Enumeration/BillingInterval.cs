namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class BillingInterval : Enumeration<BillingInterval, byte>
    {
        [Display]
        public static readonly BillingInterval Daily = new(nameof(Daily), 0, 1);

        [Display]
        public static readonly BillingInterval Weekly = new(nameof(Weekly), 1, 7);

        [Display]
        public static readonly BillingInterval Monthly = new(nameof(Monthly), 2, 30);

        [Display]
        public static readonly BillingInterval Seasonally = new(nameof(Seasonally), 3, 90);

        [Display]
        public static readonly BillingInterval Yearly = new(nameof(Yearly), 4, 365);

        public int Days { get; }

        public BillingInterval()
        {
        }

        public BillingInterval(string name, byte value, int days) : base(name, value) => Days = days;

        public DateTimeOffset CalculateEndDate(DateTimeOffset start) => start.AddDays(Days);
    }
}
