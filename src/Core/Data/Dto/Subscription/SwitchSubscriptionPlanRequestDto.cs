namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class SwitchSubscriptionPlanRequestDto
    {
        public required long UserId { get; set; }
        public required long SubscriptionPlanId { get; set; }
    }
}
