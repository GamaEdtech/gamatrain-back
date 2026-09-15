namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    public sealed class VerifyResponseDto
    {
        public string? Mint { get; set; }
        public string? SourceWallet { get; set; }

        /// <summary>The gateway's own recurring-subscription id, set only for a subscription-mode purchase on a gateway that supports recurring billing (Stripe) - <see langword="null"/> for every other verify call.</summary>
        public string? ExternalSubscriptionId { get; set; }

        /// <summary>
        /// The gateway's own id for the invoice this Checkout Session settled (Stripe: <c>Session.InvoiceId</c>) -
        /// set only for a subscription-mode purchase on a gateway that supports recurring billing, null for
        /// every other verify call. <c>PaymentService.VerifyAsync</c> persists this as the first period's
        /// <c>Payment.TransactionId</c> instead of the Checkout Session id, so the very first period is recorded
        /// under the *same* id scheme every later renewal/reconciliation uses - see <c>SubscriptionStatusResponseDto.
        /// LatestInvoiceIsFirstPeriod</c>'s doc for why a mismatched id scheme let a reconciliation job silently
        /// double-record this exact charge in production. Falls back to the Checkout Session id when null.
        /// </summary>
        public string? ExternalInvoiceId { get; set; }
    }
}
