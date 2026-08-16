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
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Subscription;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class SubscriptionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<SubscriptionService>> localizer
        , Lazy<ILogger<SubscriptionService>> logger, Lazy<IPaymentService> paymentService, Lazy<IConfiguration> configuration
        , Lazy<ISubscriptionQuotaService> subscriptionQuotaService, Lazy<IGenericFactory<IRecurringPaymentGatewayProvider, PaymentGateway>> recurringGatewayFactory
        , Lazy<IIdentityService> identityService, Lazy<IEmailService> emailService, Lazy<IApplicationSettingsService> applicationSettingsService)
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
                    Prices = t.Prices.Select(p => new SubscriptionPlanPriceDto
                    {
                        Id = p.Id,
                        SubscriptionPlanId = p.SubscriptionPlanId,
                        CountryCode = p.CountryCode,
                        Currency = p.Currency,
                        Price = p.Price,
                        BillingInterval = p.BillingInterval,
                    }).ToList(),
                }).ToListAsync();

                await AttachFeatureGroupsAsync(uow, lst);
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
                    Prices = t.Prices.Select(p => new SubscriptionPlanPriceDto
                    {
                        Id = p.Id,
                        SubscriptionPlanId = p.SubscriptionPlanId,
                        CountryCode = p.CountryCode,
                        Currency = p.Currency,
                        Price = p.Price,
                        BillingInterval = p.BillingInterval,
                    }).ToList(),
                }).FirstOrDefaultAsync();

                if (subscriptionPlan is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },] };
                }

                await AttachFeatureGroupsAsync(uow, [subscriptionPlan]);
                return new(OperationResult.Succeeded) { Data = subscriptionPlan };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// One batched query for however many plans were just fetched (not N+1), then grouped in-memory by
        /// FeatureGroupKey and attached - avoids relying on EF Core to translate a nested GroupBy inside the
        /// plans' own Select projection.
        /// </summary>
        private static async Task AttachFeatureGroupsAsync(IUnitOfWork uow, IReadOnlyCollection<SubscriptionPlanDto> plans)
        {
            var planIds = plans.Select(p => p.Id).ToList();
            var rows = await uow.GetRepository<SubscriptionPlanFeature>()
                .GetManyQueryable(f => planIds.Contains(f.SubscriptionPlanId))
                .Select(f => new
                {
                    f.SubscriptionPlanId,
                    f.FeatureId,
                    FeatureCode = f.Feature!.Code,
                    FeatureName = f.Feature.Name,
                    FeatureDescription = f.Feature.Description,
                    f.BillingInterval,
                    f.Limit,
                    f.FeatureGroupKey,
                    f.FeatureGroupDescription,
                })
                .ToListAsync();

            foreach (var plan in plans)
            {
                // A group has one row per (FeatureId x BillingInterval) it was defined at - collapse those back
                // down to a distinct feature list and a distinct per-interval limit list.
                plan.FeatureGroups = rows.Where(f => f.SubscriptionPlanId == plan.Id)
                    .GroupBy(f => f.FeatureGroupKey ?? $"single:{f.FeatureId}")
                    .Select(g =>
                    {
                        var first = g.First();
                        return new PlanFeatureGroupDto
                        {
                            Features = g.GroupBy(f => f.FeatureId)
                                .Select(f => new PlanFeatureDto { FeatureId = f.Key, FeatureCode = f.First().FeatureCode, FeatureName = f.First().FeatureName })
                                .ToList(),
                            Limits = g.GroupBy(f => f.BillingInterval)
                                .Select(l => new PlanFeatureLimitDto { BillingInterval = l.Key, Limit = l.First().Limit })
                                .ToList(),
                            // Resolved once here: the pool's description when pooled, else this feature's own -
                            // callers never need to choose between two description fields themselves.
                            Description = first.FeatureGroupDescription ?? first.FeatureDescription,
                        };
                    })
                    .ToList();
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

        public async Task<ResultData<IEnumerable<PlanFeatureGroupDto>>> GetPlanFeaturesAsync(long subscriptionPlanId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rows = await uow.GetRepository<SubscriptionPlanFeature>().GetManyQueryable(t => t.SubscriptionPlanId == subscriptionPlanId)
                    .Select(t => new
                    {
                        t.FeatureId,
                        FeatureCode = t.Feature!.Code,
                        FeatureName = t.Feature.Name,
                        FeatureDescription = t.Feature.Description,
                        t.BillingInterval,
                        t.Limit,
                        t.FeatureGroupKey,
                        t.FeatureGroupDescription,
                    })
                    .ToListAsync();

                // Grouped in-memory against the same small per-plan result set - no extra round trip. Mirrors
                // SetPlanFeaturesAsync's write shape (FeatureGroups: [{ FeatureIds, Limits, Description }]).
                // A group has one row per (FeatureId x BillingInterval) it was defined at - collapse those back
                // down to a distinct feature list and a distinct per-interval limit list.
                var lst = rows.GroupBy(t => t.FeatureGroupKey ?? $"single:{t.FeatureId}")
                    .Select(g =>
                    {
                        var first = g.First();
                        return new PlanFeatureGroupDto
                        {
                            Features = g.GroupBy(t => t.FeatureId)
                                .Select(f => new PlanFeatureDto { FeatureId = f.Key, FeatureCode = f.First().FeatureCode, FeatureName = f.First().FeatureName })
                                .ToList(),
                            Limits = g.GroupBy(t => t.BillingInterval)
                                .Select(l => new PlanFeatureLimitDto { BillingInterval = l.Key, Limit = l.First().Limit })
                                .ToList(),
                            // Resolved once here: the pool's description when pooled, else this feature's own.
                            Description = first.FeatureGroupDescription ?? first.FeatureDescription,
                        };
                    }).ToList();
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
                var allFeatureIds = requestDto.FeatureGroups.SelectMany(g => g.FeatureIds).ToList();
                if (requestDto.FeatureGroups.Any(g => !g.FeatureIds.Any()) || allFeatureIds.Count != allFeatureIds.Distinct().Count())
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidFeatureGroup"] },] };
                }

                // A pool has no single feature to describe it - unlike a single-feature entry, which already
                // has Feature.Description to display, so Description isn't required there.
                if (requestDto.FeatureGroups.Any(g => g.FeatureIds.Count() > 1 && string.IsNullOrWhiteSpace(g.Description)))
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["FeatureGroupDescriptionRequired"] },] };
                }

                // Each group needs at most one Limit entry per BillingInterval - two conflicting numbers for the
                // same interval would be ambiguous (which one wins?).
                if (requestDto.FeatureGroups.Any(g => g.Limits.Select(l => l.BillingInterval).Distinct().Count() != g.Limits.Count()))
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["DuplicateBillingIntervalInFeatureGroup"] },] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SubscriptionPlanFeature>();

                var existing = await repository.GetManyQueryable(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId, tracking: true).ToListAsync();
                foreach (var item in existing)
                {
                    repository.Remove(item);
                }

                foreach (var group in requestDto.FeatureGroups)
                {
                    var featureIds = group.FeatureIds.ToList();
                    // Server-generated, never admin-typed: a group of 2+ features pools onto one quota via a shared
                    // key that's purely a DB implementation detail - callers express pooling as FeatureIds, not a key.
                    // The same key is reused across every interval row of the group - which features are pooled
                    // together is interval-invariant, only the Limit number varies per interval.
                    var featureGroupKey = featureIds.Count > 1 ? Guid.NewGuid().ToString("N") : null;
                    foreach (var featureId in featureIds)
                    {
                        foreach (var limit in group.Limits)
                        {
                            repository.Add(new SubscriptionPlanFeature
                            {
                                SubscriptionPlanId = requestDto.SubscriptionPlanId,
                                FeatureId = featureId,
                                BillingInterval = limit.BillingInterval,
                                Limit = limit.Limit,
                                FeatureGroupKey = featureGroupKey,
                                FeatureGroupDescription = featureGroupKey is null ? null : group.Description,
                            });
                        }
                    }
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
                    BillingInterval = t.BillingInterval,
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
                    price.BillingInterval = requestDto.BillingInterval;
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
                        BillingInterval = requestDto.BillingInterval,
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
                        .GetManyQueryable(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId && t.BillingInterval == requestDto.BillingInterval
                            && (t.CountryCode == requestDto.CountryCode || t.CountryCode == null))
                        .ToListAsync();
                    price = candidates.Find(t => t.CountryCode == requestDto.CountryCode) ?? candidates.Find(t => t.CountryCode is null);
                }
                else
                {
                    price = await repository.GetAsync(t => t.SubscriptionPlanId == requestDto.SubscriptionPlanId
                        && t.BillingInterval == requestDto.BillingInterval && t.CountryCode == null);
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
                            BillingInterval = price.BillingInterval,
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

                // Defensive guard, not just a frontend convention: a fresh purchase while an Active
                // subscription already exists is exactly how a user ends up double-billed - two independent
                // Stripe subscriptions, each renewing (and charging) on its own schedule, with nothing anywhere
                // that merges or coordinates them. Found 2026-08-15 in production (a user with simultaneously
                // Active Alpha + Beta subscriptions, both auto-renewing via Stripe). The correct action when a
                // subscription already exists is SwitchSubscriptionPlanAsync (POST subscriptions/me/switch),
                // which mutates the existing row in place instead of inserting a new one - never letting this
                // method proceed past that state is deliberate: relying solely on the caller (frontend) to
                // always route correctly is how the bug happened in the first place.
                var hasActiveSubscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == requestDto.UserId && t.Status == UserSubscriptionStatus.Active)
                    .AnyAsync();
                if (hasActiveSubscription)
                {
                    return new(OperationResult.Duplicate) { Errors = [new() { Message = Localizer.Value["AlreadyHasActiveSubscription"] },] };
                }

                var priceResult = await ResolvePriceAsync(new() { SubscriptionPlanId = requestDto.SubscriptionPlanId, BillingInterval = requestDto.BillingInterval });
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
                    BillingInterval = priceResult.Data.BillingInterval,
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
                    // A gateway that supports native recurring billing (Stripe) uses this to look up its
                    // SubscriptionPlanGatewayMapping and create a real recurring subscription instead of a
                    // one-time charge; ignored by gateways that don't (GamaTrain).
                    SubscriptionPlanPriceId = priceResult.Data.Id,
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

        public async Task<ResultData<SubscriptionActionResultDto>> CancelSubscriptionAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == userId && t.Status == UserSubscriptionStatus.Active)
                    .OrderByDescending(t => t.ExpirationDate)
                    .Select(t => new
                    {
                        t.Id,
                        t.ExternalSubscriptionId,
                        t.CancelAtPeriodEnd,
                        t.ExpirationDate,
                        PlanTitle = t.SubscriptionPlan!.Title,
                        // The gateway never changes mid-subscription - any one linked Payment's Gateway is the
                        // subscription's own, avoiding a separate Gateway column on UserSubscription itself.
                        Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync();

                if (subscription is null)
                {
                    return new(OperationResult.NotFound) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                if (subscription.ExternalSubscriptionId is null)
                {
                    // A one-time/GamaTrain subscription was never going to renew - nothing for the user to
                    // cancel, it already stops at its own ExpirationDate by design.
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SubscriptionNotRecurring"] },] };
                }

                if (subscription.CancelAtPeriodEnd)
                {
                    // Already requested - idempotent no-op, not an error, and nothing changed so no email.
                    return new(OperationResult.Succeeded) { Data = new() { Success = true } };
                }

                // Shouldn't happen - ExternalSubscriptionId is only ever set right after a Payment was recorded
                // for this same subscription - guarded rather than risking an NRE/wrong-provider lookup below.
                var provider = subscription.Gateway is null ? null : recurringGatewayFactory.Value.GetProvider(subscription.Gateway);
                if (provider is null)
                {
                    return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                var cancelResult = await provider.CancelSubscriptionAsync(subscription.ExternalSubscriptionId);
                if (cancelResult.OperationResult is not OperationResult.Succeeded)
                {
                    // Don't set the local flag if the gateway call failed - stay consistent with Stripe's actual state.
                    return new(cancelResult.OperationResult) { Data = new() { Success = false }, Errors = cancelResult.Errors };
                }

                var localResult = await subscriptionQuotaService.Value.RequestCancellationAsync(subscription.Id);
                var emailNotification = localResult.OperationResult is OperationResult.Succeeded && subscription.ExpirationDate.HasValue
                    ? new SubscriptionEmailRequestDto
                    {
                        UserId = userId,
                        PlanTitle = subscription.PlanTitle ?? string.Empty,
                        ExpirationDate = subscription.ExpirationDate.Value,
                    }
                    : null;
                return new(localResult.OperationResult) { Data = new() { Success = localResult.Data, EmailNotification = emailNotification }, Errors = localResult.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SubscriptionSwitchResultDto>> SwitchSubscriptionPlanAsync([NotNull] SwitchSubscriptionPlanRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == requestDto.UserId && t.Status == UserSubscriptionStatus.Active)
                    .OrderByDescending(t => t.ExpirationDate)
                    .Select(t => new
                    {
                        t.Id,
                        t.ExternalSubscriptionId,
                        t.SubscriptionPlanId,
                        t.PricePaid,
                        t.Currency,
                        t.BillingInterval,
                        t.CancelAtPeriodEnd,
                        Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync();

                if (subscription is null)
                {
                    return new(OperationResult.NotFound) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                if (subscription.ExternalSubscriptionId is null)
                {
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SubscriptionNotRecurring"] },] };
                }

                // Omitted BillingInterval keeps the subscription's current one - the original, still-default
                // behavior. When set, this is either a plan+interval switch in one call, or (targetPlanId ==
                // current SubscriptionPlanId) a bare interval move on the same plan - e.g. Monthly -> Yearly for
                // the reason explained on the DTO: per-interval quota limits (since 2026-08-13) mean a bigger
                // interval can grant meaningfully more quota, not just a different price.
                var targetInterval = requestDto.BillingInterval ?? subscription.BillingInterval;

                if (requestDto.SubscriptionPlanId == subscription.SubscriptionPlanId && targetInterval == subscription.BillingInterval)
                {
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SamePlanSwitchNotAllowed"] },] };
                }

                if (subscription.CancelAtPeriodEnd)
                {
                    // A pending cancellation must be reversed (me/resume) before switching - avoids compounding
                    // "switch to plan B, but also ending soon" into one ambiguous state.
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SwitchNotAllowedWhileCancellationPending"] },] };
                }

                var targetPlan = await uow.GetRepository<SubscriptionPlan>().GetAsync(requestDto.SubscriptionPlanId);
                if (targetPlan is null)
                {
                    return new(OperationResult.NotFound) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },] };
                }

                if (!targetPlan.IsActive)
                {
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["PlanNotAvailable"] },] };
                }

                // Resolve the target plan's price at the target interval (== current interval unless the
                // caller asked to also move interval, above).
                var priceResult = await ResolvePriceAsync(new() { SubscriptionPlanId = requestDto.SubscriptionPlanId, BillingInterval = targetInterval });
                if (priceResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(priceResult.OperationResult) { Data = new() { Success = false }, Errors = priceResult.Errors };
                }

                if (priceResult.Data!.Currency != subscription.Currency)
                {
                    // Only matters once regional pricing goes live - not building unused cross-currency
                    // proration/credit logic now.
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SwitchCurrencyMismatch"] },] };
                }

                var mapping = await uow.GetRepository<SubscriptionPlanGatewayMapping>()
                    .GetManyQueryable(t => t.SubscriptionPlanPriceId == priceResult.Data.Id && t.Gateway == subscription.Gateway)
                    .Select(t => new { t.ExternalPlanId })
                    .FirstOrDefaultAsync();
                if (mapping?.ExternalPlanId is null)
                {
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["RecurringGatewayMappingMissing"], }] };
                }

                // Equal price is treated as a downgrade - no additional payment is being taken, so there's no
                // forfeited-value reason to apply it immediately. A bigger interval's total price is always
                // numerically greater than a smaller one's for the same plan, so this same comparison already
                // classifies "move to a bigger interval" as immediate with no separate rule needed.
                var immediate = priceResult.Data.Price > subscription.PricePaid;

                if (!immediate && targetInterval != subscription.BillingInterval)
                {
                    // Deliberately not supported yet, not silently mishandled: the deferred/schedule path has
                    // no PendingSwitchBillingInterval to carry an interval change through to
                    // RenewSubscriptionAsync, and unused already-paid-for time on a longer interval (e.g.
                    // Yearly -> Monthly mid-year) raises a refund/credit policy question this codebase has never
                    // had to answer - see docs/business/subscriptions.md, "Plan upgrade/downgrade with
                    // proration". Only a move to a bigger interval (which always resolves immediate, above) is
                    // supported for now.
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["IntervalDowngradeNotSupported"] },] };
                }

                var provider = subscription.Gateway is null ? null : recurringGatewayFactory.Value.GetProvider(subscription.Gateway);
                if (provider is null)
                {
                    return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                // Claimed BEFORE the gateway call, not after - a second, concurrent request (double-click,
                // client retry after a perceived timeout) must never reach Stripe at all, since the upgrade
                // path bills immediately (ProrationBehavior = "always_invoice") and Stripe's own idempotency key
                // is regenerated fresh on every call, so it provides no protection against a genuine duplicate
                // request. Guarded conditional UPDATE, same concurrency-safety pattern as quota consumption -
                // affected == 0 means either a switch is already in flight or one just claimed the row a moment
                // ago; either way this request must not also call Stripe. Short TTL, not tied to completion, so
                // a failed attempt (network error, declined card) doesn't block the user from retrying forever.
                var now = DateTimeOffset.UtcNow;
                var lockClaimed = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == subscription.Id && t.Status == UserSubscriptionStatus.Active
                        && (t.SwitchLockedUntil == null || t.SwitchLockedUntil < now))
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.SwitchLockedUntil, now.AddSeconds(30)));
                if (lockClaimed == 0)
                {
                    return new(OperationResult.Duplicate) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SwitchAlreadyInProgress"] },] };
                }

                var switchResult = await provider.SwitchSubscriptionPlanAsync(subscription.ExternalSubscriptionId, mapping.ExternalPlanId, immediate);
                if (switchResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(switchResult.OperationResult) { Data = new() { Success = false }, Errors = switchResult.Errors };
                }

                var localResult = immediate
                    ? await subscriptionQuotaService.Value.ApplyPlanSwitchAsync(subscription.Id, requestDto.SubscriptionPlanId, priceResult.Data.Price, targetInterval)
                    // Deferred never carries an interval change - guarded above (rejected outright otherwise),
                    // so targetInterval == subscription.BillingInterval is guaranteed here.
                    : await subscriptionQuotaService.Value.RequestPlanSwitchAsync(subscription.Id, requestDto.SubscriptionPlanId, priceResult.Data.Price);

                // Immediate: effective right now. Deferred: effective once the current period's already-set
                // ExpirationDate is reached - re-read it fresh since the projection above predates this change.
                var effectiveDate = immediate
                    ? DateTimeOffset.UtcNow
                    : await uow.GetRepository<UserSubscription>().GetManyQueryable(t => t.Id == subscription.Id).Select(t => t.ExpirationDate).FirstOrDefaultAsync();

                var emailNotification = localResult.OperationResult is OperationResult.Succeeded && effectiveDate.HasValue
                    ? new SubscriptionEmailRequestDto
                    {
                        UserId = requestDto.UserId,
                        PlanTitle = targetPlan.Title ?? string.Empty,
                        ExpirationDate = effectiveDate.Value,
                    }
                    : null;

                return new(localResult.OperationResult)
                {
                    Data = new() { Success = localResult.Data, Immediate = immediate, EffectiveDate = effectiveDate, EmailNotification = emailNotification },
                    Errors = localResult.Errors,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SubscriptionActionResultDto>> ResumeSubscriptionAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == userId && t.Status == UserSubscriptionStatus.Active)
                    .OrderByDescending(t => t.ExpirationDate)
                    .Select(t => new
                    {
                        t.Id,
                        t.ExternalSubscriptionId,
                        t.CancelAtPeriodEnd,
                        t.ExpirationDate,
                        PlanTitle = t.SubscriptionPlan!.Title,
                        Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync();

                if (subscription is null)
                {
                    return new(OperationResult.NotFound) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                if (subscription.ExternalSubscriptionId is null)
                {
                    return new(OperationResult.NotValid) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["SubscriptionNotRecurring"] },] };
                }

                if (!subscription.CancelAtPeriodEnd)
                {
                    // Nothing pending to reverse - idempotent no-op, not an error, and nothing changed so no email.
                    return new(OperationResult.Succeeded) { Data = new() { Success = true } };
                }

                var provider = subscription.Gateway is null ? null : recurringGatewayFactory.Value.GetProvider(subscription.Gateway);
                if (provider is null)
                {
                    return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                var resumeResult = await provider.ResumeSubscriptionAsync(subscription.ExternalSubscriptionId);
                if (resumeResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(resumeResult.OperationResult) { Data = new() { Success = false }, Errors = resumeResult.Errors };
                }

                var localResult = await subscriptionQuotaService.Value.ResumeSubscriptionAsync(subscription.Id);
                var emailNotification = localResult.OperationResult is OperationResult.Succeeded && subscription.ExpirationDate.HasValue
                    ? new SubscriptionEmailRequestDto
                    {
                        UserId = userId,
                        PlanTitle = subscription.PlanTitle ?? string.Empty,
                        ExpirationDate = subscription.ExpirationDate.Value,
                    }
                    : null;
                return new(localResult.OperationResult) { Data = new() { Success = localResult.Data, EmailNotification = emailNotification }, Errors = localResult.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = new() { Success = false }, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task SendSubscriptionCancelledEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto)
        {
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SubscriptionCancelledEmailTemplate))).Data;
            await SendSubscriptionEmailAsync(requestDto, template, "Gamatrain Subscription - Cancellation Scheduled");
        }

        public async Task SendSubscriptionResumedEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto)
        {
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SubscriptionResumedEmailTemplate))).Data;
            await SendSubscriptionEmailAsync(requestDto, template, "Gamatrain Subscription - Resumed");
        }

        public async Task SendSubscriptionSwitchedEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto)
        {
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SubscriptionSwitchedEmailTemplate))).Data;
            await SendSubscriptionEmailAsync(requestDto, template, "Gamatrain Subscription - Plan Changed");
        }

        private async Task SendSubscriptionEmailAsync(SubscriptionEmailRequestDto requestDto, string? template, string subject)
        {
            var user = await identityService.Value.GetUserAsync(new IdEqualsSpecification<ApplicationUser, long>(requestDto.UserId));
            if (user.Data is null)
            {
                // Best-effort notification - the cancel/resume action itself already succeeded and isn't
                // rolled back for a missing/failed email.
                return;
            }

            var name = $"{user.Data.FirstName} {user.Data.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Dear User";
            }

            template = template?
                .Replace("[RECEIVER_NAME]", name, StringComparison.OrdinalIgnoreCase)
                .Replace("[PLAN_TITLE]", requestDto.PlanTitle, StringComparison.OrdinalIgnoreCase)
                .Replace("[DATE]", requestDto.ExpirationDate.ToString(), StringComparison.OrdinalIgnoreCase);
            if (template is null || user.Data.Email is null)
            {
                return;
            }

            _ = await emailService.Value.SendEmailAsync(new()
            {
                Subject = subject,
                Body = template,
                EmailAddresses = [user.Data.Email],
                From = emailService.Value.GetNoReplyEmail(),
            });
        }

        public async Task<ResultData<ListDataSource<AdminUserSubscriptionDto>>> GetUserSubscriptionsAsync(ListRequestDto<UserSubscription>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<UserSubscription>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                // Inlined (not a helper method call) so EF Core can translate the whole projection - including
                // the User/SubscriptionPlan/Payments navigations - into one SQL query with joins. A method-call
                // Select isn't translatable, so EF would instead fetch bare entities with unloaded navigations
                // and invoke the method client-side per row, NREing on every one of those null navigations.
                var lst = await result.List.Select(t => new AdminUserSubscriptionDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    UserEmail = t.User!.Email,
                    SubscriptionPlanId = t.SubscriptionPlanId,
                    PlanTitle = t.SubscriptionPlan!.Title,
                    Status = t.Status,
                    CreationDate = t.CreationDate,
                    StartDate = t.StartDate,
                    ExpirationDate = t.ExpirationDate,
                    PricePaid = t.PricePaid,
                    Currency = t.Currency,
                    BillingInterval = t.BillingInterval,
                    AutoRenews = t.ExternalSubscriptionId != null,
                    CancelAtPeriodEnd = t.CancelAtPeriodEnd,
                    PendingSwitchPlanId = t.PendingSwitchSubscriptionPlanId,
                    PendingSwitchPlanTitle = t.PendingSwitchSubscriptionPlan!.Title,
                    LastPaymentFailedDate = t.LastPaymentFailedDate,
                    ExternalSubscriptionId = t.ExternalSubscriptionId,
                    Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                }).ToListAsync();

                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<UserSubscriptionHistoryDto>>> GetUserSubscriptionHistoryAsync(long userId, PagingDto? pagingDto = null)
        {
            try
            {
                var specification = new UserIdEqualsSpecification<UserSubscription, long>(userId)
                    .And(new UserSubscriptionStatusEqualsSpecification(UserSubscriptionStatus.Expired)
                        .Or(new UserSubscriptionStatusEqualsSpecification(UserSubscriptionStatus.Cancelled)));

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<UserSubscription>().GetManyQueryable(specification).FilterListAsync(pagingDto);
                var lst = await result.List.Select(t => new UserSubscriptionHistoryDto
                {
                    Id = t.Id,
                    SubscriptionPlanId = t.SubscriptionPlanId,
                    PlanTitle = t.SubscriptionPlan!.Title,
                    Status = t.Status,
                    CreationDate = t.CreationDate,
                    StartDate = t.StartDate,
                    ExpirationDate = t.ExpirationDate,
                    PricePaid = t.PricePaid,
                    Currency = t.Currency,
                    BillingInterval = t.BillingInterval,
                    AutoRenews = t.ExternalSubscriptionId != null,
                }).ToListAsync();

                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<AdminUserSubscriptionDto>> GetUserSubscriptionAsync([NotNull] ISpecification<UserSubscription> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>().GetManyQueryable(specification).Select(t => new AdminUserSubscriptionDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    UserEmail = t.User!.Email,
                    SubscriptionPlanId = t.SubscriptionPlanId,
                    PlanTitle = t.SubscriptionPlan!.Title,
                    Status = t.Status,
                    CreationDate = t.CreationDate,
                    StartDate = t.StartDate,
                    ExpirationDate = t.ExpirationDate,
                    PricePaid = t.PricePaid,
                    Currency = t.Currency,
                    BillingInterval = t.BillingInterval,
                    AutoRenews = t.ExternalSubscriptionId != null,
                    CancelAtPeriodEnd = t.CancelAtPeriodEnd,
                    PendingSwitchPlanId = t.PendingSwitchSubscriptionPlanId,
                    PendingSwitchPlanTitle = t.PendingSwitchSubscriptionPlan!.Title,
                    LastPaymentFailedDate = t.LastPaymentFailedDate,
                    ExternalSubscriptionId = t.ExternalSubscriptionId,
                    Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                }).FirstOrDefaultAsync();

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

        public async Task<ResultData<long>> GrantSubscriptionAsync([NotNull] GrantUserSubscriptionRequestDto requestDto) => await subscriptionQuotaService.Value.GrantSubscriptionAsync(requestDto);

        public async Task<ResultData<bool>> RevokeUserSubscriptionAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId)
                    .Select(t => new
                    {
                        t.ExternalSubscriptionId,
                        Gateway = t.Payments.Select(p => p.Gateway).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync();

                if (subscription is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                // Recurring subscription - tell the gateway to stop billing immediately before flipping local
                // state, same "gateway first, then local" order CancelSubscriptionAsync (self-service) uses.
                if (subscription.ExternalSubscriptionId is not null)
                {
                    var provider = subscription.Gateway is null ? null : recurringGatewayFactory.Value.GetProvider(subscription.Gateway);
                    if (provider is null)
                    {
                        return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                    }

                    var terminateResult = await provider.TerminateSubscriptionAsync(subscription.ExternalSubscriptionId);
                    if (terminateResult.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(terminateResult.OperationResult) { Data = false, Errors = terminateResult.Errors };
                    }
                }

                var localResult = await subscriptionQuotaService.Value.CancelSubscriptionAsync(userSubscriptionId);
                return new(localResult.OperationResult) { Data = localResult.Data, Errors = localResult.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ExtendUserSubscriptionAsync(long userSubscriptionId, int days) => await subscriptionQuotaService.Value.ExtendSubscriptionAsync(userSubscriptionId, days);

        public async Task<ResultData<ListDataSource<SubscriptionUsageEventDto>>> GetUsageHistoryAsync(ListRequestDto<SubscriptionQuotaConsumptionLog>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SubscriptionQuotaConsumptionLog>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                // Inlined (not a helper method) so EF Core can translate the whole projection - including the
                // UserSubscription/User/SubscriptionPlan/Feature navigations - into one SQL query with joins.
                var lst = await result.List.Select(t => new SubscriptionUsageEventDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    UserEmail = t.UserSubscription!.User!.Email,
                    UserSubscriptionId = t.UserSubscriptionId,
                    SubscriptionPlanId = t.UserSubscription.SubscriptionPlanId,
                    PlanTitle = t.UserSubscription.SubscriptionPlan!.Title,
                    FeatureId = t.FeatureId,
                    FeatureCode = t.Feature!.Code,
                    FeatureName = t.Feature.Name,
                    Amount = t.Amount,
                    IdentifierId = t.IdentifierId,
                    CreationDate = t.CreationDate,
                }).ToListAsync();

                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IEnumerable<SubscriptionUsageAggregateDto>>> GetUsageAggregateAsync([NotNull] GetUsageAggregateRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = await uow.GetRepository<SubscriptionQuotaConsumptionLog>()
                    .GetManyQueryable(t => t.CreationDate >= requestDto.FromDate && t.CreationDate <= requestDto.ToDate
                        && (requestDto.UserId == null || t.UserId == requestDto.UserId))
                    .GroupBy(t => new { t.FeatureId, t.Feature!.Code, t.Feature.Name })
                    .Select(g => new SubscriptionUsageAggregateDto
                    {
                        FeatureId = g.Key.FeatureId,
                        FeatureCode = g.Key.Code,
                        FeatureName = g.Key.Name,
                        TotalAmount = g.Sum(t => t.Amount),
                        EventCount = g.Count(),
                        DistinctUserCount = g.Select(t => t.UserId).Distinct().Count(),
                    })
                    .OrderByDescending(t => t.TotalAmount)
                    .ToListAsync();

                return new(OperationResult.Succeeded) { Data = lst };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
