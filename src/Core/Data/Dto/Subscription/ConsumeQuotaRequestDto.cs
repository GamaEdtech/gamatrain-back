namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ConsumeQuotaRequestDto
    {
        public required long UserId { get; set; }
        public required string FeatureCode { get; set; }
        public int Amount { get; set; } = 1;

        /// <summary>Which content item this consumption is for (e.g. a pastpaper/test/exam id) - optional, mirrors Transaction.IdentifierId. Recorded on the resulting SubscriptionQuotaConsumptionLog row when set.</summary>
        public long? IdentifierId { get; set; }
    }
}
