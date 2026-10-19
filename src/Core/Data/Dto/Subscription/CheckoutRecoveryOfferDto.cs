namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>What the offer page shows before the user is sent to the discounted checkout.</summary>
    public sealed class CheckoutRecoveryOfferDto
    {
        public long SubscriptionPlanId { get; set; }
        public string? PlanTitle { get; set; }
        public BillingInterval? BillingInterval { get; set; }
        public int DiscountPercent { get; set; }
        public decimal Price { get; set; }
        public decimal DiscountedPrice { get; set; }
        public Currency? Currency { get; set; }
        public DateTimeOffset ExpirationDate { get; set; }
    }
}
