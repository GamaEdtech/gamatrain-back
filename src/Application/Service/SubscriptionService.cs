namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class SubscriptionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<SubscriptionService>> localizer
        , Lazy<ILogger<SubscriptionService>> logger, Lazy<IPaymentService> paymentService, Lazy<IConfiguration> configuration)
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

        public async Task<ResultData<ListDataSource<FeatureDto>>> GetFeaturesAsync(ListRequestDto<Feature>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Feature, int>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new FeatureDto
                {
                    Id = t.Id,
                    Code = t.Code,
                    Name = t.Name,
                    Description = t.Description,
                    IsActive = t.IsActive,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public IReadOnlyList<string> GetFeatureCodes() =>
            [.. typeof(FeatureCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)];

        public async Task<ResultData<int>> ManageFeatureAsync([NotNull] ManageFeatureRequestDto requestDto)
        {
            try
            {
                if (requestDto.Code is not null && !GetFeatureCodes().Contains(requestDto.Code, StringComparer.Ordinal))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidFeatureCode"] },] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Feature, int>();
                Feature? feature;

                if (requestDto.Id.HasValue)
                {
                    feature = await repository.GetAsync(requestDto.Id.Value);
                    if (feature is null)
                    {
                        return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["FeatureNotFound"] },] };
                    }

                    feature.Code = requestDto.Code ?? feature.Code;
                    feature.Name = requestDto.Name ?? feature.Name;
                    feature.Description = requestDto.Description ?? feature.Description;
                    feature.IsActive = requestDto.IsActive ?? feature.IsActive;
                    _ = repository.Update(feature);
                }
                else
                {
                    feature = new Feature
                    {
                        Code = requestDto.Code,
                        Name = requestDto.Name,
                        Description = requestDto.Description,
                        IsActive = requestDto.IsActive.GetValueOrDefault(),
                        CreationDate = DateTimeOffset.UtcNow,
                    };
                    repository.Add(feature);
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = feature.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RemoveFeatureAsync([NotNull] ISpecification<Feature> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Feature, int>();
                var feature = await repository.GetAsync(specification);
                if (feature is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["FeatureNotFound"] },] };
                }

                repository.Remove(feature);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IEnumerable<PlanFeatureDto>>> GetPlanFeaturesAsync(long subscriptionPlanId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = await uow.GetRepository<SubscriptionPlanFeature>().GetManyQueryable(t => t.SubscriptionPlanId == subscriptionPlanId)
                    .Select(t => new PlanFeatureDto { FeatureId = t.FeatureId, FeatureCode = t.Feature!.Code, FeatureName = t.Feature.Name, Limit = t.Limit })
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = lst };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> SetPlanFeaturesAsync([NotNull] SetPlanFeaturesRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanFeature>();

                var existing = await repository.GetManyQueryable(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId, tracking: true).ToListAsync();
                foreach (var item in existing)
                {
                    repository.Remove(item);
                }

                foreach (var item in requestDto.Features)
                {
                    repository.Add(new SubscriptionPlanFeature { SubscriptionPlanId = requestDto.SubscriptionPlanId, FeatureId = item.FeatureId, Limit = item.Limit });
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<SubscriptionPlanPriceDto>>> GetPlanPricesAsync(ListRequestDto<SubscriptionPlanPrice>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SubscriptionPlanPrice>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new SubscriptionPlanPriceDto
                {
                    Id = t.Id,
                    SubscriptionPlanId = t.SubscriptionPlanId,
                    CountryCode = t.CountryCode,
                    Currency = t.Currency,
                    Price = t.Price,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManagePlanPriceAsync([NotNull] ManageSubscriptionPlanPriceRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanPrice>();
                SubscriptionPlanPrice? price;

                if (requestDto.Id.HasValue)
                {
                    price = await repository.GetAsync(requestDto.Id.Value);
                    if (price is null)
                    {
                        return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SubscriptionPlanPriceNotFound"] },] };
                    }

                    price.CountryCode = requestDto.CountryCode;
                    price.Currency = requestDto.Currency;
                    price.Price = requestDto.Price;
                    _ = repository.Update(price);
                }
                else
                {
                    price = new SubscriptionPlanPrice
                    {
                        SubscriptionPlanId = requestDto.SubscriptionPlanId,
                        CountryCode = requestDto.CountryCode,
                        Currency = requestDto.Currency,
                        Price = requestDto.Price,
                    };
                    repository.Add(price);
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = price.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RemovePlanPriceAsync([NotNull] ISpecification<SubscriptionPlanPrice> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanPrice>();
                var price = await repository.GetAsync(specification);
                if (price is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["SubscriptionPlanPriceNotFound"] },] };
                }

                repository.Remove(price);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<GatewayMappingDto>>> GetGatewayMappingsAsync(ListRequestDto<SubscriptionPlanGatewayMapping>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SubscriptionPlanGatewayMapping>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new GatewayMappingDto
                {
                    Id = t.Id,
                    SubscriptionPlanPriceId = t.SubscriptionPlanPriceId,
                    Gateway = t.Gateway,
                    ExternalProductId = t.ExternalProductId,
                    ExternalPlanId = t.ExternalPlanId,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageGatewayMappingAsync([NotNull] ManageGatewayMappingRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanGatewayMapping>();
                SubscriptionPlanGatewayMapping? mapping;

                if (requestDto.Id.HasValue)
                {
                    mapping = await repository.GetAsync(requestDto.Id.Value);
                    if (mapping is null)
                    {
                        return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["GatewayMappingNotFound"] },] };
                    }

                    mapping.Gateway = requestDto.Gateway;
                    mapping.ExternalProductId = requestDto.ExternalProductId;
                    mapping.ExternalPlanId = requestDto.ExternalPlanId;
                    _ = repository.Update(mapping);
                }
                else
                {
                    mapping = new SubscriptionPlanGatewayMapping
                    {
                        SubscriptionPlanPriceId = requestDto.SubscriptionPlanPriceId,
                        Gateway = requestDto.Gateway,
                        ExternalProductId = requestDto.ExternalProductId,
                        ExternalPlanId = requestDto.ExternalPlanId,
                    };
                    repository.Add(mapping);
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = mapping.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RemoveGatewayMappingAsync([NotNull] ISpecification<SubscriptionPlanGatewayMapping> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanGatewayMapping>();
                var mapping = await repository.GetAsync(specification);
                if (mapping is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["GatewayMappingNotFound"] },] };
                }

                repository.Remove(mapping);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SubscriptionPlanPriceDto>> ResolvePriceAsync([NotNull] ResolvePriceRequestDto requestDto)
        {
            try
            {
                var regionalPricingEnabled = configuration.Value.GetValue<bool>("Subscription:RegionalPricingEnabled");
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanPrice>();

                SubscriptionPlanPrice? price;
                if (regionalPricingEnabled && !string.IsNullOrEmpty(requestDto.CountryCode))
                {
                    // Fetch both candidates in one round trip; prefer the country-specific row, fall back to the global default.
                    var candidates = await repository
                        .GetManyQueryable(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId && (t.CountryCode == requestDto.CountryCode || t.CountryCode == null))
                        .ToListAsync();
                    price = candidates.Find(t => t.CountryCode == requestDto.CountryCode) ?? candidates.Find(t => t.CountryCode is null);
                }
                else
                {
                    price = await repository.GetAsync(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId && t.CountryCode == null);
                }

                return price is null
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SubscriptionPlanPriceNotFound"] },] }
                    : new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Id = price.Id,
                            SubscriptionPlanId = price.SubscriptionPlanId,
                            CountryCode = price.CountryCode,
                            Currency = price.Currency,
                            Price = price.Price,
                        },
                    };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<PurchaseSubscriptionResponseDto>> PurchaseSubscriptionAsync([NotNull] PurchaseSubscriptionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var plan = await uow.GetRepository<SubscriptionPlan>().GetAsync(requestDto.SubscriptionPlanId);
                if (plan is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },] };
                }

                if (!plan.IsActive)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["PlanNotAvailable"] },] };
                }

                var priceResult = await ResolvePriceAsync(new() { SubscriptionPlanId = requestDto.SubscriptionPlanId });
                if (priceResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(priceResult.OperationResult) { Errors = priceResult.Errors };
                }

                var subscriptionRepository = uow.GetRepository<UserSubscription>();
                var subscription = new UserSubscription
                {
                    UserId = requestDto.UserId,
                    SubscriptionPlanId = requestDto.SubscriptionPlanId,
                    Status = UserSubscriptionStatus.Pending,
                    CreationDate = DateTimeOffset.UtcNow,
                    PricePaid = priceResult.Data!.Price,
                    Currency = priceResult.Data.Currency,
                };
                subscriptionRepository.Add(subscription);
                _ = await uow.SaveChangesAsync();

                var paymentResult = await paymentService.Value.CreatePaymentAsync(new()
                {
                    UserId = requestDto.UserId,
                    Amount = priceResult.Data.Price,
                    Currency = priceResult.Data.Currency,
                    Gateway = requestDto.Gateway,
                    Title = plan.Title,
                    Description = $"Subscription: {plan.Title}",
                    UserSubscriptionId = subscription.Id,
                });

                if (paymentResult.OperationResult is not OperationResult.Succeeded)
                {
                    _ = await subscriptionRepository.GetManyQueryable(t => t.Id == subscription.Id && t.Status == UserSubscriptionStatus.Pending)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, UserSubscriptionStatus.Cancelled));
                    return new(paymentResult.OperationResult) { Errors = paymentResult.Errors };
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        UserSubscriptionId = subscription.Id,
                        PaymentId = paymentResult.Data!.PaymentId,
                        Url = paymentResult.Data.Url,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
