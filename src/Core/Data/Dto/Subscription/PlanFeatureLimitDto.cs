namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>One quota bucket's allowance at one purchased billing interval - e.g. a plan can grant 50 for Monthly and 600 for Annual of the same feature/group.</summary>
    public sealed class PlanFeatureLimitDto
    {
        public required BillingInterval BillingInterval { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        public int? Limit { get; set; }
    }
}
