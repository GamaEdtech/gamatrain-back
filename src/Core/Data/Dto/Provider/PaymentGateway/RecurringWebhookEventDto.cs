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

        /// <summary>The gateway's own subscription object ended (cancelled, or its retries were exhausted).</summary>
        SubscriptionEnded,
    }

    public sealed class RecurringWebhookEventDto
    {
        public required RecurringWebhookEventType EventType { get; set; }

        /// <summary>Resolved from the event's own metadata - never a DB lookup at this layer.</summary>
        public long? UserSubscriptionId { get; set; }

        /// <summary>The gateway's invoice id (for <see cref="RecurringWebhookEventType.InvoicePaid"/>) - becomes <c>Payment.TransactionId</c>, the idempotency key against redelivery.</summary>
        public string? ExternalTransactionId { get; set; }
    }
}
