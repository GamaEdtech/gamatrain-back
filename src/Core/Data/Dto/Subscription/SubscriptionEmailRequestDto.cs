namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>Background-job payload for the cancel/resume notification emails - deliberately just primitives (Hangfire serializes the call), not the full entity.</summary>
    public sealed class SubscriptionEmailRequestDto
    {
        public required long UserId { get; set; }
        public required string PlanTitle { get; set; }
        public required DateTimeOffset ExpirationDate { get; set; }
    }
}
