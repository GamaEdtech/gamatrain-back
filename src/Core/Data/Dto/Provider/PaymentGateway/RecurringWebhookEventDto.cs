namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    /// <summary>
    /// Not a persisted/API-facing domain concept (unlike <c>PaymentGateway</c>/<c>Currency</c>'s smart
    /// enumerations) - purely an internal signal for <c>PaymentService.HandleRecurringWebhookAsync</c> to act on,
    /// so a plain enum is enough here (same reasoning as <c>Constants.OperationResult</c>).
    /// </summary>
    public enum RecurringWebhookEventType
    {
        /// <summary>Signature verified but the event type isn't one this integration acts on.</summary>
        Ignored,

        /// <summary>An invoice - first period or a renewal - was paid.</summary>
        InvoicePaid,

        /// <summary>
        /// An immediate plan/interval switch's prorated invoice was paid (Stripe: <c>BillingReason ==
        /// "subscription_update"</c>) - a real charge, distinct from both the first-period invoice (handled by
        /// the client-driven verify flow) and an ordinary <see cref="InvoicePaid"/> renewal. Unlike
        /// <see cref="InvoicePaid"/>, doesn't represent a new billing period: the plan/price/quota change was
        /// already applied synchronously when the switch was requested (<c>SubscriptionQuotaService.
        /// ApplyPlanSwitchAsync</c>), so this only needs to record the payment, never touch
        /// <c>ExpirationDate</c> or reset quota.
        /// </summary>
        PlanChangeInvoicePaid,

        /// <summary>The gateway's own subscription object ended (cancelled, or its retries were exhausted).</summary>
        SubscriptionEnded,

        /// <summary>A renewal charge failed - the gateway's own dunning/Smart Retries are still ongoing (not yet exhausted, or this would instead have been <see cref="SubscriptionEnded"/>). Visibility only - see <c>UserSubscription.LastPaymentFailedDate</c>.</summary>
        PaymentFailed,
    }

    public sealed class RecurringWebhookEventDto
    {
        public required RecurringWebhookEventType EventType { get; set; }

        /// <summary>Resolved from the event's own metadata - never a DB lookup at this layer.</summary>
        public long? UserSubscriptionId { get; set; }

        /// <summary>The gateway's invoice id (for <see cref="RecurringWebhookEventType.InvoicePaid"/>) - becomes <c>Payment.TransactionId</c>, the idempotency key against redelivery.</summary>
        public string? ExternalTransactionId { get; set; }

        /// <summary>
        /// The invoice's own actually-charged amount (for <see cref="RecurringWebhookEventType.
        /// PlanChangeInvoicePaid"/> only) - never the subscription's own snapshotted <c>PricePaid</c>, which by
        /// the time this webhook arrives has already been overwritten to the *new* plan's full price by
        /// <c>ApplyPlanSwitchAsync</c>, not the prorated difference this specific invoice actually charged.
        /// </summary>
        public decimal? Amount { get; set; }
    }
}
