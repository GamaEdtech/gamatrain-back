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
    }
}
