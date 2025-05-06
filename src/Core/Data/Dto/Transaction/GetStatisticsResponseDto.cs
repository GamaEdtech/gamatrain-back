namespace GamaEdtech.Data.Dto.Transaction
{
    public sealed class GetStatisticsResponseDto
    {
        public string Name { get; set; }
        public int DebitValue { get; set; }
        public int CreditValue { get; set; }
    }
}
