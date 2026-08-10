namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Subscription;

    [Injectable]
    public interface ISubscriptionQuotaService
    {
        /// <summary>Activates a Pending user subscription (idempotent) and snapshots its per-feature quota rows from the plan.</summary>
        Task<ResultData<bool>> ActivateSubscriptionAsync([NotNull] ActivateUserSubscriptionRequestDto requestDto);

        /// <summary>Native-recurring-billing renewal (idempotent, no-op if not Active): extends ExpirationDate one more BillingInterval and resets the existing quota buckets' Used back to 0 - the same UserSubscription row keeps renewing rather than a new row per period.</summary>
        Task<ResultData<bool>> RenewSubscriptionAsync(long userSubscriptionId);

        /// <summary>Native-recurring-billing end signal (idempotent, guarded on Active): flips the subscription Cancelled, driven by the gateway's own subscription-ended webhook event, not a user-facing cancel endpoint.</summary>
        Task<ResultData<bool>> CancelSubscriptionAsync(long userSubscriptionId);

        /// <summary>Attempts to consume quota for a feature; a non-consumed result is not an error, see <see cref="ConsumeQuotaResponseDto"/>.</summary>
        Task<ResultData<ConsumeQuotaResponseDto>> ConsumeQuotaAsync([NotNull] ConsumeQuotaRequestDto requestDto);

        Task<ResultData<UserSubscriptionDto>> GetCurrentSubscriptionAsync(long userId);

        /// <summary>Flips overdue Active subscriptions to Expired. Hangfire recurring job target.</summary>
        Task<ResultData<int>> ExpireOverdueSubscriptionsAsync();
    }
}
