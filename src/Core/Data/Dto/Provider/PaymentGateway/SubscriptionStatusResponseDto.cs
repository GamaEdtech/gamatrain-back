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
        /// True only when <see cref="LatestInvoiceId"/> is a genuine renewal (Stripe: <c>Invoice.BillingReason ==
        /// "subscription_cycle"</c>). A reconciling caller (<c>SubscriptionQuotaService.SyncExpirationFromGatewayAsync</c>)
        /// records a renewal <c>Payment</c> for this id only when this is <see langword="true"/>. Every other kind of
        /// latest invoice must not be recorded as one: the first invoice (<c>"subscription_create"</c>) was already
        /// recorded by the original purchase flow, and an immediate plan switch's prorated invoice
        /// (<c>"subscription_update"</c>) belongs to <c>PaymentService.HandlePlanChangeInvoicePaidAsync</c>. This used
        /// to be a negative check (only <c>"subscription_create"</c> excluded), which let the nightly reconciliation
        /// record a plan switch's $89.88 proration as a $199 "Renewal" in production (2026-09-28) - see
        /// docs/business/subscriptions.md, "Reconciliation recorded a plan switch as a renewal".
        /// </summary>
        public bool LatestInvoiceIsRenewal { get; set; }

        /// <summary>
        /// True only when <see cref="LatestInvoiceId"/> has actually been paid (Stripe: <c>Invoice.Status == "paid"</c>).
        /// Stripe rolls a subscription into its new period - <c>Status</c> still "active", <see cref="CurrentPeriodEnd"/>
        /// already the new period's end - as soon as the cycle's invoice is finalized, before its charge collects (a payment
        /// whose funds Stripe is still confirming can stay pending for up to 4 business days). So "active with a future period end"
        /// does not mean the period was paid for; a reconciling caller must not renew on it unless this is
        /// <see langword="true"/>. False as well when the gateway reported no latest invoice at all. A 100%-discounted
        /// invoice is "paid" with a zero amount, so it still counts. See docs/business/subscriptions.md, "Reconciliation
        /// renewed a subscription whose invoice was still unpaid".
        /// </summary>
        public bool LatestInvoiceIsPaid { get; set; }

        /// <summary>
        /// What <see cref="LatestInvoiceId"/> actually charged (Stripe: <c>Invoice.AmountPaid</c>, converted from
        /// minor units), so a reconciled renewal is recorded at the real amount rather than the subscription's own
        /// snapshotted <c>PricePaid</c> (which can differ - a coupon, a pending downgrade, a price change). Null if the
        /// gateway didn't report one; the caller then falls back to <c>PricePaid</c>.
        /// </summary>
        public decimal? LatestInvoiceAmountPaid { get; set; }
    }
}
