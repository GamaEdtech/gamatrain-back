namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class Period : Enumeration<Period, byte>
    {
        [Display]
        public static readonly Period DayOfWeek = new(nameof(DayOfWeek), 0);

        [Display]
        public static readonly Period MonthOfYear = new(nameof(MonthOfYear), 1);

        public Period()
        {
        }

        public Period(string name, byte value) : base(name, value)
        {
        }
    }
}
