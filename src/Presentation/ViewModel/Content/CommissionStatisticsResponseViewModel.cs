namespace GamaEdtech.Presentation.ViewModel.Content
{
    public sealed class CommissionStatisticsResponseViewModel
    {
        /// <summary>Bucketed by day-of-week or month, scoped to [StartDate, EndDate] per Period - the chart data.</summary>
        public IEnumerable<CommissionStatisticsBucketResponseViewModel> Statistics { get; set; } = [];

        /// <summary>Lifetime commission balance - NOT scoped by StartDate/EndDate/Period, unlike Statistics above.</summary>
        public decimal TotalAmountUsd { get; set; }

        /// <summary>Lifetime Points equivalent of TotalAmountUsd - same lifetime scope, not affected by the filter.</summary>
        public long TotalPoints { get; set; }
    }
}
