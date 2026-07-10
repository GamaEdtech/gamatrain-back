namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class SubscriptionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<SubscriptionService>> localizer
        , Lazy<ILogger<SubscriptionService>> logger)
        : LocalizableServiceBase<SubscriptionService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISubscriptionService
    {
        public async Task<ResultData<ListDataSource<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(ListRequestDto<SubscriptionPlan>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SubscriptionPlan>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new SubscriptionPlanDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Polygon = t.Polygon,
                    IsActive = t.IsActive,
                    Highlight = t.Highlight,
                    BillingInterval = t.BillingInterval,
                    Prices = t.Prices.Select(p => new SubscriptionPlanPriceDto
                    {
                        Id = p.Id,
                        SubscriptionPlanId = p.SubscriptionPlanId,
                        CountryCode = p.CountryCode,
                        Currency = p.Currency,
                        Price = p.Price,
                    }).ToList(),
                    Features = t.PlanFeatures.Select(f => new PlanFeatureDto
                    {
                        FeatureId = f.FeatureId,
                        FeatureCode = f.Feature!.Code,
                        FeatureName = f.Feature.Name,
                        Limit = f.Limit,
                    }).ToList(),
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SubscriptionPlanDto>> GetSubscriptionPlanAsync([NotNull] ISpecification<SubscriptionPlan> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscriptionPlan = await uow.GetRepository<SubscriptionPlan>().GetManyQueryable(specification).Select(t => new SubscriptionPlanDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Polygon = t.Polygon,
                    IsActive = t.IsActive,
                    Highlight = t.Highlight,
                    BillingInterval = t.BillingInterval,
                    Prices = t.Prices.Select(p => new SubscriptionPlanPriceDto
                    {
                        Id = p.Id,
                        SubscriptionPlanId = p.SubscriptionPlanId,
                        CountryCode = p.CountryCode,
                        Currency = p.Currency,
                        Price = p.Price,
                    }).ToList(),
                    Features = t.PlanFeatures.Select(f => new PlanFeatureDto
                    {
                        FeatureId = f.FeatureId,
                        FeatureCode = f.Feature!.Code,
                        FeatureName = f.Feature.Name,
                        Limit = f.Limit,
                    }).ToList(),
                }).FirstOrDefaultAsync();

                return subscriptionPlan is null
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = subscriptionPlan };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageSubscriptionPlanAsync([NotNull] ManageSubscriptionPlanRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlan>();
                SubscriptionPlan? subscriptionPlan = null;

                if (requestDto.Id.HasValue)
                {
                    subscriptionPlan = await repository.GetAsync(requestDto.Id.Value);
                    if (subscriptionPlan is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },],
                        };
                    }

                    subscriptionPlan.Title = requestDto.Title ?? subscriptionPlan.Title;
                    subscriptionPlan.Polygon = requestDto.Polygon ?? subscriptionPlan.Polygon;
                    subscriptionPlan.IsActive = requestDto.IsActive ?? subscriptionPlan.IsActive;
                    subscriptionPlan.Highlight = requestDto.Highlight ?? subscriptionPlan.Highlight;
                    subscriptionPlan.BillingInterval = requestDto.BillingInterval ?? subscriptionPlan.BillingInterval;

                    _ = repository.Update(subscriptionPlan);
                }
                else
                {
                    subscriptionPlan = new SubscriptionPlan
                    {
                        Title = requestDto.Title,
                        Polygon = requestDto.Polygon,
                        IsActive = requestDto.IsActive.GetValueOrDefault(),
                        Highlight = requestDto.Highlight.GetValueOrDefault(),
                        BillingInterval = requestDto.BillingInterval!,
                    };
                    repository.Add(subscriptionPlan);
                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = subscriptionPlan.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveSubscriptionPlanAsync([NotNull] ISpecification<SubscriptionPlan> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlan>();
                var subscriptionPlan = await repository.GetAsync(specification);
                if (subscriptionPlan is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },],
                    };
                }

                var inUse = await uow.GetRepository<UserSubscription>().GetManyQueryable(t => t.SubscriptionPlanId == subscriptionPlan.Id).AnyAsync();
                if (inUse)
                {
                    return new(OperationResult.NotValid)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["SubscriptionPlanInUse"] },],
                    };
                }

                repository.Remove(subscriptionPlan);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }
    }
}
