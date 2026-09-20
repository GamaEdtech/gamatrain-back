namespace GamaEdtech.Data.Dto.Provider.PaymentGateway
{
    /// <summary>
    /// Outcome of an <c>IRecurringPaymentGatewayProvider.SwitchSubscriptionPlanAsync</c> call - distinct from
    /// the surrounding <c>ResultData.OperationResult</c>, which only reflects whether the gateway API call
    /// itself succeeded, not whether an immediate switch's prorated charge actually collected. Found live in
    /// production (2026-09): the gateway call for an immediate upgrade can report success as soon as the price
    /// change is accepted, while the synchronous proration charge behind it still goes on to fail (a decline) -
    /// the caller was applying the plan/quota upgrade unconditionally on that first success, granting a higher
    /// tier the user never actually paid for, with no rollback when the charge failed. A deferred (downgrade)
    /// switch never bills anything now, so it always reports <see cref="PaymentConfirmed"/> true - nothing to
    /// confirm.
    /// </summary>
    public sealed class SwitchSubscriptionPlanResultDto
    {
        /// <summary>True once the immediate switch's prorated invoice is confirmed paid (Stripe: the resulting Invoice's own Status is "paid"), or there was nothing to confirm (a deferred switch).</summary>
        public required bool PaymentConfirmed { get; set; }

        /// <summary>
        /// Set whenever <see cref="PaymentConfirmed"/> is false, for the caller to surface to the user - the
        /// caller must not apply the plan/quota change while this is set. Deliberately doesn't distinguish "a
        /// declined attempt Stripe will still retry" from "genuinely exhausted" - Smart Retries schedules a
        /// future attempt after almost any decline, so at the moment of the synchronous switch call the invoice
        /// is essentially always still "open" with another attempt pending, not yet a final, no-more-retries
        /// failure; the message stays honest about that rather than confidently declaring a final failure this
        /// call can't actually see. A payment that later succeeds on one of those retries is still picked up
        /// automatically - see PaymentService.HandlePlanChangeInvoicePaidAsync's own doc comment for how.
        /// </summary>
        public string? FailureReason { get; set; }
    }
}
