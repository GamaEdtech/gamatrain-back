namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    public sealed class SubscriptionStatusResponseDto
    {
        /// <summary>The gateway's own raw status string (Stripe: "active", "past_due", "canceled", "unpaid", "incomplete", "incomplete_expired", "paused") - exposed as-is for logging/admin visibility, never parsed by the caller.</summary>
        public required string Status { get; set; }

        /// <summary>
        /// True only for Stripe's own "active" - deliberately not "trialing" (trials are out of scope for this
        /// app) or "past_due" (a subscription mid-Stripe-dunning-retry is already, deliberately, treated as
        /// usable only until its own local ExpirationDate regardless of gateway status - see
        /// docs/business/subscriptions.md, "Dunning is entirely Stripe's" - so this reconciliation must agree,
        /// not carve out an exception for "still retrying").
        /// </summary>
        public required bool IsActive { get; set; }

        /// <summary>The gateway's own current billing period end - only meaningful when <see cref="IsActive"/> is true. Null if the gateway didn't report one.</summary>
        public DateTimeOffset? CurrentPeriodEnd { get; set; }

        /// <summary>
        /// The gateway's own id for the invoice covering <see cref="CurrentPeriodEnd"/> (Stripe:
        /// <c>Subscription.LatestInvoiceId</c>) - a reconciling caller (<c>SubscriptionQuotaService.
        /// SyncExpirationFromGatewayAsync</c>) records a <c>Payment</c> keyed by this id, using the exact same
        /// <c>(TransactionId, Gateway)</c> uniqueness guard <c>PaymentService.HandleInvoicePaidAsync</c> already
        /// relies on - so if this same invoice's own <c>invoice.paid</c> webhook later arrives for real (a
        /// delayed retry, once the outage/bug that caused the original delay clears), its insert collides,
        /// gets caught as a duplicate, and correctly skips renewing/resetting quota a second time for a cycle
        /// reconciliation already caught up. Only meaningful when <see cref="IsActive"/> is true; may be null
        /// even then in principle (no invoice yet), in which case the reconciling caller degrades to syncing
        /// without recording a <c>Payment</c> - a real, if unlikely, gap for that one case.
        /// </summary>
        public string? LatestInvoiceId { get; set; }

        /// <summary>
        /// True when <see cref="LatestInvoiceId"/> is the subscription's very first invoice (Stripe:
        /// <c>Invoice.BillingReason == "subscription_create"</c>), not a genuine renewal. A subscription still on
        /// its first period - most commonly one that's cancelling at period end and so will never reach a second
        /// invoice - has its local <c>ExpirationDate</c> legitimately drift stale (see the calendar-drift fix in
        /// docs/business/subscriptions.md) without ever having missed a real renewal webhook. A reconciling
        /// caller (<c>SubscriptionQuotaService.SyncExpirationFromGatewayAsync</c>) must not record a
        /// <c>Payment</c> for this id when this is <see langword="true"/> - that invoice's charge was already
        /// recorded, under the Checkout Session id, by the original purchase flow (<c>PaymentService.
        /// VerifyAsync</c> → <c>ActivateSubscriptionAsync</c>), so inserting one keyed by the invoice id instead
        /// sails past the <c>(TransactionId, Gateway)</c> idempotency guard (different id, no collision) and
        /// records a second, phantom Payment for a charge Stripe never actually made that day. Correcting
        /// <c>ExpirationDate</c> itself is still safe and desired either way - only the Payment insert is gated
        /// on this flag.
        /// </summary>
        public bool LatestInvoiceIsFirstPeriod { get; set; }
    }
}
