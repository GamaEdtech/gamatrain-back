namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class SubscriptionQuotaService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<SubscriptionQuotaService>> localizer, Lazy<ILogger<SubscriptionQuotaService>> logger)
        : LocalizableServiceBase<SubscriptionQuotaService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISubscriptionQuotaService
    {
        public async Task<ResultData<bool>> ActivateSubscriptionAsync([NotNull] ActivateUserSubscriptionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<UserSubscription>();

                var sub = await repository.GetManyQueryable(t => t.Id == requestDto.UserSubscriptionId)
                    .Select(t => new { t.SubscriptionPlanId, PlanBillingInterval = t.SubscriptionPlan!.BillingInterval })
                    .FirstOrDefaultAsync();
                if (sub is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                var start = DateTimeOffset.UtcNow;
                var end = sub.PlanBillingInterval.CalculateEndDate(start);

                // Guarded on Pending -> zero rows affected means this activation already happened (idempotent, e.g. a re-verify race).
                var affected = await repository.GetManyQueryable(t => t.Id == requestDto.UserSubscriptionId && t.Status == UserSubscriptionStatus.Pending)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, UserSubscriptionStatus.Active)
                        .SetProperty(p => p.StartDate, start)
                        .SetProperty(p => p.ExpirationDate, end));
                if (affected == 0)
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidSubscriptionStatus"] },] };
                }

                var planFeatures = await uow.GetRepository<SubscriptionPlanFeature>()
                    .GetManyQueryable(t => t.SubscriptionPlanId == sub.SubscriptionPlanId && t.Feature!.IsActive)
                    .Select(t => new { t.FeatureId, t.Limit })
                    .ToListAsync();

                var quotaRepository = uow.GetRepository<UserSubscriptionQuota>();
                foreach (var planFeature in planFeatures)
                {
                    quotaRepository.Add(new UserSubscriptionQuota
                    {
                        UserSubscriptionId = requestDto.UserSubscriptionId,
                        FeatureId = planFeature.FeatureId,
                        Limit = planFeature.Limit,
                        Used = 0,
                    });
                }
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ConsumeQuotaResponseDto>> ConsumeQuotaAsync([NotNull] ConsumeQuotaRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var quotaRepository = uow.GetRepository<UserSubscriptionQuota>();
                var now = DateTimeOffset.UtcNow;

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var candidate = await quotaRepository.GetManyQueryable(q =>
                            q.Feature!.Code == requestDto.FeatureCode
                            && q.UserSubscription!.UserId == requestDto.UserId
                            && q.UserSubscription.Status == UserSubscriptionStatus.Active
                            && q.UserSubscription.ExpirationDate > now
                            && q.Used + requestDto.Amount <= q.Limit)
                        .OrderBy(q => q.UserSubscription!.ExpirationDate)
                        .Select(q => new { q.Id, q.Limit, q.Used })
                        .FirstOrDefaultAsync();

                    if (candidate is null)
                    {
                        break;
                    }

                    // Guard re-checked in the WHERE clause: safe under concurrent consumption of the same row.
                    var affected = await quotaRepository.GetManyQueryable(q => q.Id == candidate.Id && q.Used + requestDto.Amount <= q.Limit)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.Used, p => p.Used + requestDto.Amount));
                    if (affected == 1)
                    {
                        return new(OperationResult.Succeeded)
                        {
                            Data = new() { Consumed = true, RemainingQuota = candidate.Limit - candidate.Used - requestDto.Amount },
                        };
                    }
                    // Lost a race against a concurrent consumer; retry once against a fresh read.
                }

                var hasActiveSubscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(s => s.UserId == requestDto.UserId && s.Status == UserSubscriptionStatus.Active && s.ExpirationDate > now)
                    .AnyAsync();

                QuotaFailureReason reason;
                var currentLimit = 0;
                if (!hasActiveSubscription)
                {
                    reason = QuotaFailureReason.NoActiveSubscription;
                }
                else
                {
                    var existingQuota = await quotaRepository.GetManyQueryable(q =>
                            q.Feature!.Code == requestDto.FeatureCode
                            && q.UserSubscription!.UserId == requestDto.UserId
                            && q.UserSubscription.Status == UserSubscriptionStatus.Active
                            && q.UserSubscription.ExpirationDate > now)
                        .Select(q => new { q.Limit })
                        .FirstOrDefaultAsync();

                    if (existingQuota is null)
                    {
                        reason = QuotaFailureReason.FeatureNotInPlan;
                    }
                    else
                    {
                        reason = QuotaFailureReason.QuotaExhausted;
                        currentLimit = existingQuota.Limit;
                    }
                }

                var suggestions = await uow.GetRepository<SubscriptionPlanFeature>()
                    .GetManyQueryable(pf => pf.Feature!.Code == requestDto.FeatureCode && pf.SubscriptionPlan!.IsActive && pf.Limit > currentLimit)
                    .OrderBy(pf => pf.Limit)
                    .Select(pf => new UpgradeSuggestionDto { SubscriptionPlanId = pf.SubscriptionPlanId, Title = pf.SubscriptionPlan!.Title, Limit = pf.Limit })
                    .Take(3)
                    .ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new() { Consumed = false, Reason = reason, UpgradeSuggestions = suggestions },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<UserSubscriptionDto>> GetCurrentSubscriptionAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == userId && t.Status == UserSubscriptionStatus.Active)
                    .OrderByDescending(t => t.ExpirationDate)
                    .Select(t => new UserSubscriptionDto
                    {
                        Id = t.Id,
                        SubscriptionPlanId = t.SubscriptionPlanId,
                        PlanTitle = t.SubscriptionPlan!.Title,
                        Status = t.Status,
                        StartDate = t.StartDate,
                        ExpirationDate = t.ExpirationDate,
                        PricePaid = t.PricePaid,
                        Currency = t.Currency,
                        Quotas = t.Quotas.Select(q => new UserSubscriptionQuotaDto
                        {
                            FeatureCode = q.Feature!.Code,
                            FeatureName = q.Feature.Name,
                            Limit = q.Limit,
                            Used = q.Used,
                            Remaining = q.Limit - q.Used,
                        }).ToList(),
                    })
                    .FirstOrDefaultAsync();

                return subscription is null
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] }
                    : new(OperationResult.Succeeded) { Data = subscription };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<int>> ExpireOverdueSubscriptionsAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Status == UserSubscriptionStatus.Active && t.ExpirationDate < DateTimeOffset.UtcNow)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, UserSubscriptionStatus.Expired));
                return new(OperationResult.Succeeded) { Data = affected };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
