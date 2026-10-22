namespace GamaEdtech.Presentation.ViewModel.Dashboard
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>Whole UTC days, inclusive. Both optional: the default is the last 30 days; at most 366 days.</summary>
    public sealed class DashboardRangeRequestViewModel
    {
        [Display]
        public DateOnly? StartDate { get; set; }

        [Display]
        public DateOnly? EndDate { get; set; }
    }
}
