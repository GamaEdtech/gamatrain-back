namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class UserSubscriptionQuotaDto
    {
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
        public int? Limit { get; set; }
        public int Used { get; set; }

        /// <summary><see langword="null"/> means unlimited (<see cref="Limit"/> is <see langword="null"/>).</summary>
        public int? Remaining { get; set; }
    }
}
