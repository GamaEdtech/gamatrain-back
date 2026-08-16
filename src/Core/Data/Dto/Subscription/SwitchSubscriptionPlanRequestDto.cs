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

        /// <summary>
        /// Defaults <see langword="false"/>. An upgrade (plan or interval) bills the card immediately via a real
        /// Stripe proration invoice - real money moving on what looks to the caller like a single "switch my
        /// plan" click is worth a confirmation step. When the resolved switch would be immediate and this is
        /// <see langword="false"/>, nothing is applied and nothing is charged: the response instead carries a
        /// preview of the exact amount that *would* be charged (<see cref="SubscriptionSwitchResultDto.
        /// PreviewAmount"/>, computed via Stripe's own no-side-effect invoice-preview API), and the caller is
        /// expected to resubmit the identical request with <see langword="true"/> once the user has seen and
        /// confirmed that amount. A deferred switch (downgrade) never bills anything now, so this flag is
        /// irrelevant for that case - it applies immediately regardless, exactly as before this field existed.
        /// Added 2026-08-16.
        /// </summary>
        public bool Confirm { get; set; }
    }
}
