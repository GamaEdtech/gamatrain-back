namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface ISubscriptionQuotaService
    {
        /// <summary>Activates a Pending user subscription (idempotent) and snapshots its per-feature quota rows from the plan.</summary>
        Task<ResultData<bool>> ActivateSubscriptionAsync([NotNull] ActivateUserSubscriptionRequestDto requestDto);

        /// <summary>Native-recurring-billing renewal (idempotent, no-op if not Active): extends ExpirationDate one more BillingInterval and resets the existing quota buckets' Used back to 0 - the same UserSubscription row keeps renewing rather than a new row per period.</summary>
        Task<ResultData<bool>> RenewSubscriptionAsync(long userSubscriptionId);

        /// <summary>Native-recurring-billing end signal (idempotent, guarded on Active): flips the subscription Cancelled, driven by the gateway's own subscription-ended webhook event, not a user-facing cancel endpoint.</summary>
        Task<ResultData<bool>> CancelSubscriptionAsync(long userSubscriptionId);

        /// <summary>User-initiated cancellation (idempotent, guarded on Active): sets CancelAtPeriodEnd only - Status/ExpirationDate are untouched, they change later when the gateway's own subscription-ended webhook fires at the real period end (see CancelSubscriptionAsync above).</summary>
        Task<ResultData<bool>> RequestCancellationAsync(long userSubscriptionId);

        /// <summary>Reverses a pending cancellation request (idempotent, guarded on Active): clears CancelAtPeriodEnd, the subscription goes back to renewing normally.</summary>
        Task<ResultData<bool>> ResumeSubscriptionAsync(long userSubscriptionId);

        /// <summary>Attempts to consume quota for a feature; a non-consumed result is not an error, see <see cref="ConsumeQuotaResponseDto"/>.</summary>
        Task<ResultData<ConsumeQuotaResponseDto>> ConsumeQuotaAsync([NotNull] ConsumeQuotaRequestDto requestDto);

        /// <summary>
        /// Reverses a previously-successful <see cref="ConsumeQuotaAsync"/> consumption on the same
        /// <paramref name="userSubscriptionId"/>'s bucket for <paramref name="featureCode"/> - for a caller that
        /// charged quota for something it then failed to actually deliver (e.g. <c>ContentDeliveryService</c>,
        /// when gama-api's own download call fails after the local charge already succeeded). Floors at 0 (never
        /// refunds more than the bucket's current <c>Used</c>, so calling this twice for the same consumption -
        /// or after the bucket was reset by an unrelated renewal in between - can't push it negative). Writes a
        /// negative-<c>Amount</c> <c>SubscriptionQuotaConsumptionLog</c> row so admin usage reporting nets out
        /// correctly instead of showing a charge with no matching content ever delivered.
        /// </summary>
        Task<ResultData<bool>> RefundQuotaAsync(long userId, long userSubscriptionId, [NotNull] string featureCode, int amount, long? identifierId);

        Task<ResultData<UserSubscriptionDto>> GetCurrentSubscriptionAsync(long userId);

        /// <summary>
        /// Flips overdue Active subscriptions to Expired. Hangfire recurring job target. Only ever considers a
        /// subscription once it's overdue by more than a grace period (ordinary webhook jitter - confirmed live,
        /// a healthy subscription's renewal arriving consistently ~1 hour after the naive expectation - resolves
        /// on its own well within it). For a recurring (gateway-backed) subscription past the grace period, first
        /// asks the gateway directly (<c>IRecurringPaymentGatewayProvider.GetSubscriptionStatusAsync</c>) rather
        /// than trusting local state alone: if the gateway confirms it's actually still active with a future
        /// period end, <see cref="SyncExpirationFromGatewayAsync"/> instead of expiring - self-healing a missed
        /// webhook without ever touching the gateway or cutting the user off. Only expires (and best-effort
        /// terminates the gateway side) once the gateway itself confirms it's over, or there's no gateway to ask
        /// (a one-time/GamaTrain subscription). If the gateway check itself fails (network error, etc.), the
        /// subscription is left Active to be re-checked next run rather than guessing - a stuck-Active row for
        /// one more day is a far smaller problem than wrongly cancelling a paying customer's real subscription.
        /// </summary>
        Task<ResultData<int>> ExpireOverdueSubscriptionsAsync();

        /// <summary>
        /// Reconciliation counterpart to <see cref="RenewSubscriptionAsync"/> - syncs <c>ExpirationDate</c>
        /// directly to <paramref name="gatewayCurrentPeriodEnd"/> (the gateway's own reported value, not "+1
        /// BillingInterval" computed from the stale local one) and resets quota, exactly like a real renewal
        /// would have. Setting the value directly self-heals correctly even if more than one cycle was missed -
        /// one call catches all the way up to the gateway's real current period end, not just one cycle at a
        /// time. Used by <see cref="ExpireOverdueSubscriptionsAsync"/> and the admin "resync from gateway"
        /// action (<c>SubscriptionService.ResyncUserSubscriptionAsync</c>). Guarded on Active, idempotent-style
        /// no-op (<c>Data: false</c>) if the subscription isn't Active/found - same as
        /// <see cref="RenewSubscriptionAsync"/>. Deliberately does not apply a pending plan switch, unlike
        /// <see cref="RenewSubscriptionAsync"/> - a pending switch combined with a missed-webhook reconciliation
        /// is a rare-in-rare edge case; it's picked up by the next genuine renewal instead.
        /// </summary>
        /// <param name="userSubscriptionId">The subscription to sync.</param>
        /// <param name="gatewayCurrentPeriodEnd">The gateway's own reported current period end (<see
        /// cref="Data.Dto.Provider.PaymentGateway.SubscriptionStatusResponseDto.CurrentPeriodEnd"/>).</param>
        /// <param name="externalInvoiceId">
        /// The gateway's own id for the invoice covering <paramref name="gatewayCurrentPeriodEnd"/> (see
        /// <see cref="Data.Dto.Provider.PaymentGateway.SubscriptionStatusResponseDto.LatestInvoiceId"/>'s doc for
        /// why this matters) - when non-null, a <c>Payment</c> is recorded keyed by it (same
        /// <c>(TransactionId, Gateway)</c> idempotency guard <c>PaymentService.HandleInvoicePaidAsync</c> uses)
        /// <em>before</em> syncing, and a duplicate (this invoice was already recorded - by an earlier
        /// reconciliation run, or because the "missing" webhook actually arrived and was processed normally in
        /// the meantime) short-circuits the whole call as a safe no-op, exactly as it should: the cycle this
        /// call would have synced is already accounted for. Null degrades to syncing unconditionally, without
        /// recording a <c>Payment</c> or this dedup protection - only expected when the gateway itself reports
        /// no current invoice.
        /// </param>
        Task<ResultData<bool>> SyncExpirationFromGatewayAsync(long userSubscriptionId, DateTimeOffset gatewayCurrentPeriodEnd, string? externalInvoiceId);

        /// <summary>Admin-initiated comped grant for a support case: creates a new UserSubscription Active immediately (PricePaid 0, no Payment row), snapshotting quota rows exactly like ActivateSubscriptionAsync does. Returns the new subscription's id.</summary>
        Task<ResultData<long>> GrantSubscriptionAsync([NotNull] GrantUserSubscriptionRequestDto requestDto);

        /// <summary>Admin-initiated support-case extension: pushes ExpirationDate forward by the given number of days (guarded on Active). Purely a local record change - doesn't touch or re-bill the gateway side.</summary>
        Task<ResultData<bool>> ExtendSubscriptionAsync(long userSubscriptionId, int days);

        /// <summary>Immediate plan switch (upgrade): swaps SubscriptionPlanId/PricePaid/BillingInterval right away and re-snapshots quota buckets for the new plan+interval (guarded on Active), carrying forward each feature's already-consumed Used (capped to the new Limit) rather than resetting it - this is a mid-cycle proration, not a new period, so consumption already made this period must survive the switch. Clears any stale PendingSwitch* fields. newBillingInterval is the caller's already-resolved target interval - the current one unless also moving to a bigger interval (see SwitchSubscriptionPlanRequestDto.BillingInterval).</summary>
        Task<ResultData<bool>> ApplyPlanSwitchAsync(long userSubscriptionId, long newSubscriptionPlanId, decimal newPricePaid, BillingInterval newBillingInterval);

        /// <summary>Deferred plan switch (downgrade): records PendingSwitch* only (guarded on Active) - RenewSubscriptionAsync applies it at the next renewal instead of extending the current plan. newBillingInterval is the caller's already-resolved target interval - same as the subscription's current one unless this deferred switch is also moving interval (e.g. an Annual -&gt; Monthly downgrade); RenewSubscriptionAsync applies it together with the plan/price at the renewal boundary.</summary>
        Task<ResultData<bool>> RequestPlanSwitchAsync(long userSubscriptionId, long newSubscriptionPlanId, decimal newPricePaid, BillingInterval newBillingInterval);
    }
}
