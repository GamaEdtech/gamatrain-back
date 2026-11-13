namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>What a user got for one week: the report itself, the short reminder sent instead for a week with no
    /// activity, or nothing. See docs/business/notifications.md, "Weekly report".</summary>
    public sealed class WeeklyReportKind : Enumeration<WeeklyReportKind, byte>
    {
        [Display]
        public static readonly WeeklyReportKind Report = new(nameof(Report), 0);

        [Display]
        public static readonly WeeklyReportKind Reminder = new(nameof(Reminder), 1);

        /// <summary>Nothing sent: a quiet week with no reminder due. Recorded so the week's later runs don't evaluate the user
        /// again.</summary>
        [Display]
        public static readonly WeeklyReportKind Skipped = new(nameof(Skipped), 2);

        public WeeklyReportKind()
        {
        }

        public WeeklyReportKind(string name, byte value) : base(name, value)
        {
        }
    }
}
