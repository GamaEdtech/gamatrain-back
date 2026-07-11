namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class UpgradeSuggestionDto
    {
        public long SubscriptionPlanId { get; set; }
        public string? Title { get; set; }
        public int Limit { get; set; }
    }
}
