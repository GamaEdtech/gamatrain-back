namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>Admin visibility: one row of the raw consumption event log (SubscriptionQuotaConsumptionLog).</summary>
    public sealed class SubscriptionUsageEventDto
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string? UserEmail { get; set; }
        public long UserSubscriptionId { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public int FeatureId { get; set; }
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
        public int Amount { get; set; }

        /// <summary>Which content item this consumption was for (e.g. a pastpaper/test/exam id) - null for events recorded before this field existed, or a future call site that doesn't supply one.</summary>
        public long? IdentifierId { get; set; }
        public DateTimeOffset CreationDate { get; set; }
    }
}
