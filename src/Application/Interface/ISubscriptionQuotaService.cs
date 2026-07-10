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

        /// <summary>Attempts to consume quota for a feature; a non-consumed result is not an error, see <see cref="ConsumeQuotaResponseDto"/>.</summary>
        Task<ResultData<ConsumeQuotaResponseDto>> ConsumeQuotaAsync([NotNull] ConsumeQuotaRequestDto requestDto);

        Task<ResultData<UserSubscriptionDto>> GetCurrentSubscriptionAsync(long userId);

        /// <summary>Flips overdue Active subscriptions to Expired. Hangfire recurring job target.</summary>
        Task<ResultData<int>> ExpireOverdueSubscriptionsAsync();
    }
}
