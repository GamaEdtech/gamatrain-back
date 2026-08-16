namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SwitchSubscriptionPlanRequestDto
    {
        public required long UserId { get; set; }
        public required long SubscriptionPlanId { get; set; }

        /// <summary>
        /// Optional - omitted means "keep my current interval", the original (and still default) behavior. Set
        /// to move the subscription to a different interval, with or without also changing
        /// <see cref="SubscriptionPlanId"/> (e.g. "same plan, but Yearly instead of Monthly" - added 2026-08-16
        /// specifically because per-interval quota limits, since 2026-08-13, mean a bigger interval can grant
        /// meaningfully more quota, not just a different price, making an interval-only move a genuine upgrade
        /// in the same sense a plan-tier change already is). Only a move to a *bigger* interval (by resolved
        /// price - same immediate/deferred rule as a plan switch, unchanged) is supported for now; a move to a
        /// smaller interval is rejected outright rather than silently mishandled, since the deferred/schedule
        /// path has no <c>PendingSwitchBillingInterval</c> to carry it through to renewal, and unused
        /// already-paid-for time on a longer interval raises a refund/credit policy question this codebase has
        /// never had to answer - see docs/business/subscriptions.md, "Plan upgrade/downgrade with proration".
        /// </summary>
        public BillingInterval? BillingInterval { get; set; }
    }
}
