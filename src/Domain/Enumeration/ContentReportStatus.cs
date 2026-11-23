namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Where a "report a problem" is (<c>ContentReports.Status</c>). Stored: never renumber.</summary>
    public sealed class ContentReportStatus : Enumeration<ContentReportStatus, byte>
    {
        /// <summary>Waiting for staff.</summary>
        [Display]
        public static readonly ContentReportStatus Open = new(nameof(Open), 0);

        /// <summary>The problem was fixed.</summary>
        [Display]
        public static readonly ContentReportStatus Resolved = new(nameof(Resolved), 1);

        /// <summary>Not a problem, or not one we fix.</summary>
        [Display]
        public static readonly ContentReportStatus Dismissed = new(nameof(Dismissed), 2);

        public ContentReportStatus()
        {
        }

        public ContentReportStatus(string name, byte value)
            : base(name, value)
        {
        }
    }
}
