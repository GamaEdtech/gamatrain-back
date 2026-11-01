namespace GamaEdtech.Presentation.ViewModel.Dashboard
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Whole days in <see cref="TimeZone"/>, inclusive. Both optional: the default is the last 30 days; at most 366 days.</summary>
    public sealed class DashboardRangeRequestViewModel
    {
        [Display]
        public DateOnly? StartDate { get; set; }

        [Display]
        public DateOnly? EndDate { get; set; }

        /// <summary>The viewer's time zone (IANA or Windows id), which the days are in. Omitted or unknown: UTC.</summary>
        [Display]
        public string? TimeZone { get; set; }
    }
}
