namespace GamaEdtech.Data.Dto.Content
{
    public sealed class GetCommissionStatisticsResponseDto
    {
        /// <summary>Bucketed by day-of-week or month, scoped to [StartDate, EndDate] per Period - the chart data.</summary>
        public IEnumerable<CommissionStatisticsBucketDto> Statistics { get; set; } = [];

        /// <summary>
        /// Lifetime sum of every ContentOwnerCommission row this owner has ever accrued - deliberately NOT
        /// scoped by StartDate/EndDate/Period, unlike Statistics above. Since ContentOwnerCommission carries
        /// no paid/payout state yet (see the entity's own doc comment), this is currently the owner's entire
        /// available balance.
        /// </summary>
        public decimal TotalAmountUsd { get; set; }

        /// <summary>Lifetime sum of Points across every ContentOwnerCommission row - same lifetime scope as TotalAmountUsd, just the points-denominated equivalent.</summary>
        public long TotalPoints { get; set; }
    }
}
