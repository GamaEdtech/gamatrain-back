namespace GamaEdtech.Data.Dto.Transaction
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class GetStatisticsRequestDto
    {
        public int UserId { get; set; }
        public Period Period { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }
}
