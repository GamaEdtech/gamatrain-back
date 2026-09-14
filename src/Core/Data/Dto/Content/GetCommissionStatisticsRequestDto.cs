namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class GetCommissionStatisticsRequestDto
    {
        public long UserId { get; set; }
        public Period Period { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }
}
