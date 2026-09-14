namespace GamaEdtech.Data.Dto.Content
{
    /// <summary>One bucket (a day-of-week or a month, per GetCommissionStatisticsRequestDto.Period) of the commission-statistics report - see GetCommissionStatisticsResponseDto.</summary>
    public sealed class CommissionStatisticsBucketDto
    {
        public string? Name { get; set; }
        public decimal AmountUsd { get; set; }
        public long Points { get; set; }
    }
}
