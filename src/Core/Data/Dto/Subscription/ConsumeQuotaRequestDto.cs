namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ConsumeQuotaRequestDto
    {
        public required long UserId { get; set; }
        public required string FeatureCode { get; set; }
        public int Amount { get; set; } = 1;
    }
}
