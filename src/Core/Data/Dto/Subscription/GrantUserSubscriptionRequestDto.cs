namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Admin-initiated, comped subscription grant for a support case - bypasses payment entirely
    /// (PricePaid is always recorded as 0), so it never goes through the normal Pending -> Payment ->
    /// VerifyAsync -> Activate flow; it's created Active immediately.
    /// </summary>
    public sealed class GrantUserSubscriptionRequestDto
    {
        public required long UserId { get; set; }
        public required long SubscriptionPlanId { get; set; }

        /// <summary>Determines the granted period's length (same CalculateEndDate used by a real purchase).</summary>
        public required BillingInterval BillingInterval { get; set; }
    }
}
