namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSubscriptionDto
    {
        public long Id { get; set; }
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public UserSubscriptionStatus? Status { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
        public decimal PricePaid { get; set; }
        public Currency? Currency { get; set; }
        public BillingInterval? BillingInterval { get; set; }
        public IEnumerable<UserSubscriptionQuotaDto>? Quotas { get; set; }
    }
}
