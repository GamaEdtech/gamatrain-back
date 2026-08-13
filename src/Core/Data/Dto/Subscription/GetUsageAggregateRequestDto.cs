namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>Same endpoint serves both a per-user and a global usage dashboard - UserId unset means "every user".</summary>
    public sealed class GetUsageAggregateRequestDto
    {
        public long? UserId { get; set; }
        public required DateTimeOffset FromDate { get; set; }
        public required DateTimeOffset ToDate { get; set; }
    }
}
