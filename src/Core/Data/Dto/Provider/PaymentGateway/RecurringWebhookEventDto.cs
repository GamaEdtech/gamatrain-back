namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    using GamaEdtech.Domain.Enumeration;

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
        /// <see cref="InvoicePaid"/>, doesn't represent a new billing period, so this never touches
        /// <c>ExpirationDate</c> or resets quota. The plan/price/quota change itself
        /// (<c>SubscriptionQuotaService.ApplyPlanSwitchAsync</c>) was usually already applied synchronously
        /// when the switch was requested (once request-time confirmation saw the charge succeed) - but not
        /// always: if that confirmation instead saw the charge still pending/declined and rejected the switch,
        /// and Stripe's own Smart Retries later succeed on this same invoice, this event is the *only* place
        /// that still knows to apply it - see <see cref="RecurringWebhookEventDto.TargetSubscriptionPlanId"/>.
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
        /// The gateway's own end date for the period this invoice covers (for
        /// <see cref="RecurringWebhookEventType.InvoicePaid"/> only - Stripe: the first line item's
        /// <c>Period.End</c>). Used as the new <c>UserSubscription.ExpirationDate</c> directly, in place of
        /// locally recomputing one via <c>BillingInterval.CalculateEndDate</c> - the gateway's own reported
        /// value is authoritative and immune to the calendar-length drift a fixed day-count per interval
        /// otherwise accumulates (a 31-day month, a leap year, ...). Null if the gateway didn't report one
        /// (shouldn't happen in practice for a genuine <c>subscription_cycle</c> invoice), in which case the
        /// caller falls back to the old calculation.
        /// </summary>
        public DateTimeOffset? PeriodEnd { get; set; }

        /// <summary>
        /// The invoice's own actually-charged amount (for <see cref="RecurringWebhookEventType.
        /// PlanChangeInvoicePaid"/> only) - never the subscription's own snapshotted <c>PricePaid</c>, which by
        /// the time this webhook arrives may already have been overwritten to the *new* plan's full price by
        /// <c>ApplyPlanSwitchAsync</c> (if the switch was confirmed and applied synchronously), not the
        /// prorated difference this specific invoice actually charged.
        /// </summary>
        public decimal? Amount { get; set; }

        /// <summary>
        /// The plan the switch that generated this invoice was moving to (for <see cref="RecurringWebhookEventType.
        /// PlanChangeInvoicePaid"/> only) - read back from the same Subscription metadata
        /// <see cref="UserSubscriptionId"/> is, set by <c>StripePaymentGatewayProvider.
        /// SwitchSubscriptionPlanAsync</c> at request time so it survives even when request-time confirmation
        /// couldn't apply the switch itself yet (the charge was still pending/declined then). Null for every
        /// other event type, and also null here if the metadata is missing/corrupt - in which case the caller
        /// can only fall back to recording the payment, the same as before this field existed.
        /// </summary>
        public long? TargetSubscriptionPlanId { get; set; }

        /// <summary>Paired with <see cref="TargetSubscriptionPlanId"/> - the price snapshot <c>ApplyPlanSwitchAsync</c> should record on <c>UserSubscription.PricePaid</c>, not this invoice's own prorated <see cref="Amount"/>.</summary>
        public decimal? TargetPricePaid { get; set; }

        /// <summary>Paired with <see cref="TargetSubscriptionPlanId"/> - the billing interval <c>ApplyPlanSwitchAsync</c> should record, matching whatever was actually resolved/priced/sent to the gateway at request time.</summary>
        public BillingInterval? TargetBillingInterval { get; set; }
    }
}
