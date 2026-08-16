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
                    .Select(t => new { t.SubscriptionPlanId, t.BillingInterval })
                    .FirstOrDefaultAsync();
                if (sub is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                var start = DateTimeOffset.UtcNow;
                var end = sub.BillingInterval.CalculateEndDate(start);

                // Guarded on Pending -> zero rows affected means this activation already happened (idempotent, e.g. a re-verify race).
                var affected = await repository.GetManyQueryable(t => t.Id == requestDto.UserSubscriptionId && t.Status == UserSubscriptionStatus.Pending)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, UserSubscriptionStatus.Active)
                        .SetProperty(p => p.StartDate, start)
                        .SetProperty(p => p.ExpirationDate, end)
                        .SetProperty(p => p.ExternalSubscriptionId, requestDto.ExternalSubscriptionId));
                if (affected == 0)
                {
                    return new(OperationResult.NotValid) { Data = false, Errors = [new() { Message = Localizer.Value["InvalidSubscriptionStatus"] },] };
                }

                await CreateQuotasAsync(uow, sub.SubscriptionPlanId, sub.BillingInterval, requestDto.UserSubscriptionId);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// Snapshots one quota bucket per feature group from the plan's current SubscriptionPlanFeature rows, at
        /// the subscription's own <paramref name="billingInterval"/>, onto a (already Active) UserSubscription -
        /// first deleting any quota rows it already has. The delete is a no-op for
        /// ActivateSubscriptionAsync/GrantSubscriptionAsync (a brand-new subscription never has prior quota
        /// rows); it's required for ApplyPlanSwitchAsync, which reuses this same helper on an existing
        /// subscription switching plans - without it, old and new plan sharing a FeatureId would violate
        /// UserSubscriptionQuotaFeature's (UserSubscriptionId, FeatureId) unique index. Cascades to
        /// UserSubscriptionQuotaFeature automatically (DeleteBehavior.Cascade on that FK). Saves its own changes.
        /// </summary>
        private static async Task CreateQuotasAsync(IUnitOfWork uow, long subscriptionPlanId, BillingInterval billingInterval, long userSubscriptionId)
        {
            _ = await uow.GetRepository<UserSubscriptionQuota>()
                .GetManyQueryable(t => t.UserSubscriptionId == userSubscriptionId)
                .ExecuteDeleteAsync();

            var planFeatures = await uow.GetRepository<SubscriptionPlanFeature>()
                .GetManyQueryable(t => t.SubscriptionPlanId == subscriptionPlanId && t.BillingInterval == billingInterval && t.Feature!.IsActive)
                .Select(t => new { t.FeatureId, t.Limit, t.FeatureGroupKey, t.FeatureGroupDescription, FeatureDescription = t.Feature!.Description })
                .ToListAsync();

            var quotaRepository = uow.GetRepository<UserSubscriptionQuota>();
            var quotaFeatureRepository = uow.GetRepository<UserSubscriptionQuotaFeature>();

            // One quota bucket per group; a null FeatureGroupKey is its own singleton group (unpooled feature).
            foreach (var group in planFeatures.GroupBy(pf => pf.FeatureGroupKey ?? $"single:{pf.FeatureId}"))
            {
                var first = group.First();
                var quota = new UserSubscriptionQuota
                {
                    UserSubscriptionId = userSubscriptionId,
                    Limit = first.Limit,
                    Used = 0,
                    // Resolved once here: the pool's description when pooled, else this feature's own.
                    Description = first.FeatureGroupDescription ?? first.FeatureDescription,
                };
                quotaRepository.Add(quota);

                foreach (var planFeature in group)
                {
                    quotaFeatureRepository.Add(new UserSubscriptionQuotaFeature
                    {
                        UserSubscriptionQuota = quota,
                        UserSubscriptionId = userSubscriptionId,
                        FeatureId = planFeature.FeatureId,
                    });
                }
            }
            _ = await uow.SaveChangesAsync();
        }

        public async Task<ResultData<bool>> RenewSubscriptionAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<UserSubscription>();

                var sub = await repository.GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .Select(t => new { t.BillingInterval, t.ExpirationDate, t.PendingSwitchSubscriptionPlanId, t.PendingSwitchPricePaid })
                    .FirstOrDefaultAsync();
                if (sub is null)
                {
                    // Not Active (already Expired/Cancelled, or the id doesn't exist) - a safe no-op, same
                    // "nothing left to do" idempotency style as ActivateSubscriptionAsync's zero-rows-affected case.
                    return new(OperationResult.Succeeded) { Data = false };
                }

                // Extends from the subscription's own current ExpirationDate, not "now" - keeps the billing
                // cycle anchored to its original date even if this runs a little late.
                var newExpirationDate = sub.BillingInterval.CalculateEndDate(sub.ExpirationDate ?? DateTimeOffset.UtcNow);

                // Any successful renewal - whether the very next charge after a failure, or an unrelated later
                // one - clears LastPaymentFailedDate in both branches below: a prior invoice.payment_failed
                // stopped mattering the moment a charge actually went through.

                // A pending downgrade (POST subscriptions/me/switch to a cheaper plan) applies here, at the
                // renewal boundary, instead of the usual same-plan extension - the gateway's own Subscription
                // Schedule already flipped the price at this same boundary, so local state just needs to catch up.
                if (sub.PendingSwitchSubscriptionPlanId.HasValue && sub.PendingSwitchPricePaid.HasValue)
                {
                    var switchAffected = await repository.GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                        .ExecuteUpdateAsync(t => t
                            .SetProperty(p => p.SubscriptionPlanId, sub.PendingSwitchSubscriptionPlanId.Value)
                            .SetProperty(p => p.PricePaid, sub.PendingSwitchPricePaid.Value)
                            .SetProperty(p => p.ExpirationDate, newExpirationDate)
                            .SetProperty(p => p.PendingSwitchSubscriptionPlanId, (long?)null)
                            .SetProperty(p => p.PendingSwitchPricePaid, (decimal?)null)
                            .SetProperty(p => p.LastPaymentFailedDate, (DateTimeOffset?)null));
                    if (switchAffected == 0)
                    {
                        // Lost a race - nothing to renew.
                        return new(OperationResult.Succeeded) { Data = false };
                    }

                    await CreateQuotasAsync(uow, sub.PendingSwitchSubscriptionPlanId.Value, sub.BillingInterval, userSubscriptionId);
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var affected = await repository.GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.ExpirationDate, newExpirationDate)
                        .SetProperty(p => p.LastPaymentFailedDate, (DateTimeOffset?)null));
                if (affected == 0)
                {
                    // Lost a race (e.g. cancelled between the read above and this update) - nothing to renew.
                    return new(OperationResult.Succeeded) { Data = false };
                }

                _ = await uow.GetRepository<UserSubscriptionQuota>()
                    .GetManyQueryable(t => t.UserSubscriptionId == userSubscriptionId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.Used, 0));

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> CancelSubscriptionAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, UserSubscriptionStatus.Cancelled));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RequestCancellationAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                // A pending downgrade doesn't make sense once cancellation is requested - the subscription is
                // ending anyway, so clear it rather than leaving it to (incorrectly) apply at a renewal that will
                // never happen. The gateway-side schedule attached by the switch is released separately, by
                // IRecurringPaymentGatewayProvider.CancelSubscriptionAsync, before this is called.
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.CancelAtPeriodEnd, true)
                        .SetProperty(p => p.PendingSwitchSubscriptionPlanId, (long?)null)
                        .SetProperty(p => p.PendingSwitchPricePaid, (decimal?)null));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ResumeSubscriptionAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.CancelAtPeriodEnd, false));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
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
                            q.Features.Any(f => f.Feature!.Code == requestDto.FeatureCode)
                            && q.UserSubscription!.UserId == requestDto.UserId
                            && q.UserSubscription.Status == UserSubscriptionStatus.Active
                            && q.UserSubscription.ExpirationDate > now
                            && (q.Limit == null || q.Used + requestDto.Amount <= q.Limit))
                        .OrderBy(q => q.UserSubscription!.ExpirationDate)
                        .Select(q => new
                        {
                            q.Id,
                            q.Limit,
                            q.Used,
                            q.UserSubscriptionId,
                            // The specific feature this event is for, even when the bucket pools several -
                            // Used above is the pooled total, but the log needs to attribute this event correctly.
                            q.Features.First(f => f.Feature!.Code == requestDto.FeatureCode).FeatureId,
                        })
                        .FirstOrDefaultAsync();

                    if (candidate is null)
                    {
                        break;
                    }

                    // Guard re-checked in the WHERE clause: safe under concurrent consumption of the same row.
                    var affected = await quotaRepository.GetManyQueryable(q => q.Id == candidate.Id && (q.Limit == null || q.Used + requestDto.Amount <= q.Limit))
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.Used, p => p.Used + requestDto.Amount));
                    if (affected == 1)
                    {
                        // Best-effort, deliberately isolated from the method's own try/catch: the decrement
                        // above already committed directly (ExecuteUpdateAsync, not part of a SaveChanges unit
                        // of work) and is the source of truth for enforcement - a failure writing the log entry
                        // must never turn a successful consumption into a reported failure (the caller would
                        // wrongly fall back to charging wallet points on top of an already-spent quota unit),
                        // only this one event's reporting trail would be missing.
                        try
                        {
                            uow.GetRepository<SubscriptionQuotaConsumptionLog>().Add(new()
                            {
                                UserId = requestDto.UserId,
                                UserSubscriptionId = candidate.UserSubscriptionId,
                                FeatureId = candidate.FeatureId,
                                Amount = requestDto.Amount,
                                IdentifierId = requestDto.IdentifierId,
                                CreationDate = now,
                            });
                            _ = await uow.SaveChangesAsync();
                        }
                        catch (Exception logExc)
                        {
                            Logger.Value.LogException(logExc);
                        }

                        return new(OperationResult.Succeeded)
                        {
                            Data = new() { Consumed = true, RemainingQuota = candidate.Limit - candidate.Used - requestDto.Amount },
                        };
                    }
                    // Lost a race against a concurrent consumer; retry once against a fresh read.
                }

                // Earliest-expiring, same tie-break ConsumeQuotaAsync's own candidate query above uses - a user
                // can have more than one Active subscription (stacking is permitted, never blocked at purchase
                // time), so this is "a" representative current subscription, not necessarily the only one. Good
                // enough for the client to know "I already have something active, route this as a switch, not a
                // fresh purchase" - see docs/business/subscriptions.md, "Quota consumption and the points
                // fallback" and the CurrentSubscriptionId doc comment on ConsumeQuotaResponseDto.
                var activeSubscription = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(s => s.UserId == requestDto.UserId && s.Status == UserSubscriptionStatus.Active && s.ExpirationDate > now)
                    .OrderBy(s => s.ExpirationDate)
                    .Select(s => new { s.Id, s.SubscriptionPlanId, PlanTitle = s.SubscriptionPlan!.Title })
                    .FirstOrDefaultAsync();

                QuotaFailureReason reason;
                int? currentLimit = 0;
                if (activeSubscription is null)
                {
                    reason = QuotaFailureReason.NoActiveSubscription;
                }
                else
                {
                    var existingQuota = await quotaRepository.GetManyQueryable(q =>
                            q.Features.Any(f => f.Feature!.Code == requestDto.FeatureCode)
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

                // currentLimit == null means the user's existing quota is already unlimited - nothing is a better upgrade.
                // pf.Limit == null (an unlimited plan feature) always beats a finite currentLimit.
                // Matched to the plan's default (global) price at the SAME billing interval - a SubscriptionPlanFeature
                // row's Limit only applies at its own BillingInterval now, not fanned out across every interval
                // the plan is sold at (Monthly/Yearly/... can legitimately have different limits).
                var candidatesByInterval = await uow.GetRepository<SubscriptionPlanFeature>()
                    .GetManyQueryable(pf => pf.Feature!.Code == requestDto.FeatureCode && pf.SubscriptionPlan!.IsActive
                        && currentLimit != null && (pf.Limit == null || pf.Limit > currentLimit))
                    .SelectMany(pf => pf.SubscriptionPlan!.Prices.Where(pr => pr.CountryCode == null && pr.BillingInterval == pf.BillingInterval), (pf, pr) => new
                    {
                        pf.SubscriptionPlanId,
                        PlanTitle = pf.SubscriptionPlan!.Title,
                        pf.Limit,
                        pr.BillingInterval,
                    })
                    .ToListAsync();

                // Up to 3 suggestions per billing interval (Monthly, Yearly, and so on), cheapest-qualifying-first
                // within each; then regrouped by plan below so a plan offered at several intervals appears once,
                // with all of its qualifying prices nested inside instead of repeating the plan's own fields per interval.
                var survivingCandidates = candidatesByInterval
                    .GroupBy(c => c.BillingInterval)
                    .SelectMany(g => g.OrderBy(c => c.Limit == null ? 1 : 0).ThenBy(c => c.Limit).Take(3))
                    .ToList();

                // Fetched directly (not via ISubscriptionService) to avoid a circular dependency:
                // SubscriptionService -> PaymentService -> ISubscriptionQuotaService already exists,
                // so this service can't also depend on ISubscriptionService.
                var candidatePlanIds = survivingCandidates.Select(c => c.SubscriptionPlanId).Distinct();
                var suggestedPlans = survivingCandidates.Count == 0
                    ? []
                    : await uow.GetRepository<SubscriptionPlan>()
                        .GetManyQueryable(p => candidatePlanIds.Contains(p.Id))
                        .Select(p => new
                        {
                            p.Id,
                            p.Highlight,
                            Prices = p.Prices.Where(pr => pr.CountryCode == null)
                                .Select(pr => new { pr.BillingInterval, pr.Currency, pr.Price }).ToList(),
                        })
                        .ToListAsync();

                // Fetched separately (not nested in the plan projection above) and grouped in-memory - avoids
                // relying on EF Core to translate a nested GroupBy inside the plans' own Select.
                var featureRows = suggestedPlans.Count == 0
                    ? []
                    : await uow.GetRepository<SubscriptionPlanFeature>()
                        .GetManyQueryable(f => candidatePlanIds.Contains(f.SubscriptionPlanId))
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

                // Scoped to one billing interval - a plan's feature groups (and their limits) can now differ
                // between Monthly/Yearly/etc, so this can't be resolved once per plan anymore, only once per
                // (plan, interval) pair.
                List<UpgradeSuggestionFeatureGroupDto> BuildFeatureGroups(long planId, BillingInterval? billingInterval) =>
                    [.. featureRows.Where(f => f.SubscriptionPlanId == planId && f.BillingInterval == billingInterval)
                        .GroupBy(f => f.FeatureGroupKey ?? $"single:{f.FeatureId}")
                        .Select(g =>
                        {
                            var first = g.First();
                            return new UpgradeSuggestionFeatureGroupDto
                            {
                                Features = g.Select(f => new PlanFeatureDto { FeatureId = f.FeatureId, FeatureCode = f.FeatureCode, FeatureName = f.FeatureName }).ToList(),
                                Limit = first.Limit,
                                // Resolved once here: the pool's description when pooled, else this feature's own.
                                Description = first.FeatureGroupDescription ?? first.FeatureDescription,
                            };
                        })];

                var suggestions = survivingCandidates
                    .GroupBy(c => c.SubscriptionPlanId)
                    .Select(g =>
                    {
                        var first = g.First();
                        var plan = suggestedPlans.FirstOrDefault(p => p.Id == first.SubscriptionPlanId);

                        var prices = g.Select(c =>
                        {
                            var price = plan?.Prices.FirstOrDefault(pr => pr.BillingInterval == c.BillingInterval);
                            var monthlyEquivalentPrice = price is null ? (decimal?)null : price.Price / (price.BillingInterval.Days / 30m);

                            // Discount is only meaningful relative to this same plan's own Monthly price (same
                            // SubscriptionPlanId, no title/tier guessing) - never shown for the Monthly entry itself
                            // or when the plan has no Monthly price to compare against.
                            decimal? discountPercent = null;
                            if (price is not null && c.BillingInterval != BillingInterval.Monthly)
                            {
                                var monthlyPrice = plan?.Prices.FirstOrDefault(pr => pr.BillingInterval == BillingInterval.Monthly);
                                if (monthlyPrice is not null && monthlyPrice.Price > 0)
                                {
                                    discountPercent = Math.Round((1 - (monthlyEquivalentPrice!.Value / monthlyPrice.Price)) * 100, 2);
                                }
                            }

                            // Resolved per interval - this plan's feature groups (and their limits) can differ
                            // between Monthly/Yearly/etc, so "what you'd get" is computed once per (plan, interval).
                            var featureGroups = BuildFeatureGroups(first.SubscriptionPlanId, c.BillingInterval);

                            // The interval's own feature groups already resolved pooling; reuse the group covering
                            // the specific feature the caller was blocked on, so "why you're blocked" and "what
                            // you'd get" agree without a second lookup on the client.
                            var failedGroup = featureGroups.FirstOrDefault(gr => gr.Features.Any(f => f.FeatureCode == requestDto.FeatureCode));

                            return new UpgradeSuggestionPriceDto
                            {
                                BillingInterval = c.BillingInterval,
                                Currency = price?.Currency,
                                CurrencySymbol = price?.Currency.Symbol,
                                Price = price?.Price,
                                MonthlyEquivalentPrice = monthlyEquivalentPrice,
                                DiscountPercent = discountPercent,
                                Limit = c.Limit,
                                PooledFeatureCodes = failedGroup?.Features.Count() > 1
                                    ? failedGroup.Features.Where(f => f.FeatureCode != requestDto.FeatureCode).Select(f => f.FeatureCode!).ToList()
                                    : null,
                                Description = failedGroup?.Description,
                                FeatureGroups = featureGroups,
                            };
                        }).ToList();

                        return new UpgradeSuggestionDto
                        {
                            Id = first.SubscriptionPlanId,
                            Title = first.PlanTitle,
                            Highlight = plan?.Highlight ?? false,
                            Prices = prices,
                        };
                    })
                    .ToList();

                // Ready-made tab/period manifest: distinct intervals present anywhere above, in interval order,
                // so the caller doesn't have to scan every suggested plan's prices to know which periods exist.
                var availableBillingIntervals = suggestions
                    .SelectMany(s => s.Prices ?? [])
                    .Select(p => p.BillingInterval)
                    .Where(bi => bi is not null)
                    .Distinct()
                    .OrderBy(bi => bi!.Value)
                    .Select(bi => bi!.Name)
                    .ToList();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Consumed = false,
                        Reason = reason,
                        CurrentSubscriptionId = activeSubscription?.Id,
                        CurrentPlanId = activeSubscription?.SubscriptionPlanId,
                        CurrentPlanTitle = activeSubscription?.PlanTitle,
                        UpgradeSuggestions = suggestions,
                        AvailableBillingIntervals = availableBillingIntervals,
                    },
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
                    .Select(t => new
                    {
                        t.Id,
                        t.SubscriptionPlanId,
                        PlanTitle = t.SubscriptionPlan!.Title,
                        t.Status,
                        t.StartDate,
                        t.ExpirationDate,
                        t.PricePaid,
                        t.Currency,
                        t.BillingInterval,
                        AutoRenews = t.ExternalSubscriptionId != null,
                        t.CancelAtPeriodEnd,
                        PendingSwitchPlanId = t.PendingSwitchSubscriptionPlanId,
                        PendingSwitchPlanTitle = t.PendingSwitchSubscriptionPlan!.Title,
                        t.LastPaymentFailedDate,
                        Quotas = t.Quotas.Select(q => new
                        {
                            q.Limit,
                            q.Used,
                            q.Description,
                            Features = q.Features.Select(f => new { f.FeatureId, FeatureCode = f.Feature!.Code, FeatureName = f.Feature.Name }).ToList(),
                        }).ToList(),
                    })
                    .FirstOrDefaultAsync();

                if (subscription is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                // The plan's own live per-interval limits, alongside the subscriber's own snapshotted one - so a
                // client can show what the SAME plan grants at a different interval without a second round trip.
                // Fetched separately (not nested in the query above) and matched in-memory, same pattern as
                // SubscriptionService.AttachFeatureGroupsAsync - avoids relying on EF Core to translate a nested
                // GroupBy inside the subscription's own Select.
                var planFeatureRows = await uow.GetRepository<SubscriptionPlanFeature>()
                    .GetManyQueryable(pf => pf.SubscriptionPlanId == subscription.SubscriptionPlanId)
                    .Select(pf => new { pf.FeatureId, pf.BillingInterval, pf.Limit })
                    .ToListAsync();

                var dto = new UserSubscriptionDto
                {
                    Id = subscription.Id,
                    SubscriptionPlanId = subscription.SubscriptionPlanId,
                    PlanTitle = subscription.PlanTitle,
                    Status = subscription.Status,
                    StartDate = subscription.StartDate,
                    ExpirationDate = subscription.ExpirationDate,
                    PricePaid = subscription.PricePaid,
                    Currency = subscription.Currency,
                    BillingInterval = subscription.BillingInterval,
                    AutoRenews = subscription.AutoRenews,
                    CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                    PendingSwitchPlanId = subscription.PendingSwitchPlanId,
                    PendingSwitchPlanTitle = subscription.PendingSwitchPlanTitle,
                    LastPaymentFailedDate = subscription.LastPaymentFailedDate,
                    FeatureGroups = subscription.Quotas.Select(q => new UserSubscriptionQuotaDto
                    {
                        // Same resolved value as the bucket's own Description below - every feature in a
                        // pooled bucket shares it, matching the upgrade-suggestion feature-list shape.
                        Features = q.Features.Select(f => new UserSubscriptionQuotaFeatureDto
                        {
                            FeatureCode = f.FeatureCode,
                            FeatureName = f.FeatureName,
                            Description = q.Description,
                        }).ToList(),
                        Limit = q.Limit,
                        Used = q.Used,
                        Remaining = q.Limit - q.Used,
                        Description = q.Description,
                        // Matched by FeatureId, not the (possibly stale) FeatureGroupKey this bucket was
                        // snapshotted with - reflects the plan's current configuration, not the one at activation.
                        PlanLimits = planFeatureRows.Where(pf => q.Features.Any(f => f.FeatureId == pf.FeatureId))
                            .GroupBy(pf => pf.BillingInterval)
                            .Select(g => new PlanFeatureLimitDto { BillingInterval = g.Key, Limit = g.First().Limit })
                            .ToList(),
                    }).ToList(),
                };

                return new(OperationResult.Succeeded) { Data = dto };
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

        public async Task<ResultData<long>> GrantSubscriptionAsync([NotNull] GrantUserSubscriptionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                var planExists = await uow.GetRepository<SubscriptionPlan>()
                    .GetManyQueryable(t => t.Id == requestDto.SubscriptionPlanId)
                    .AnyAsync();
                if (!planExists)
                {
                    return new(OperationResult.NotFound) { Data = 0, Errors = [new() { Message = Localizer.Value["SubscriptionPlanNotFound"] },] };
                }

                var start = DateTimeOffset.UtcNow;
                var subscription = new UserSubscription
                {
                    UserId = requestDto.UserId,
                    SubscriptionPlanId = requestDto.SubscriptionPlanId,
                    Status = UserSubscriptionStatus.Active,
                    CreationDate = start,
                    StartDate = start,
                    ExpirationDate = requestDto.BillingInterval.CalculateEndDate(start),
                    // Comped - no payment was ever taken for this subscription.
                    PricePaid = 0,
                    Currency = Currency.USD,
                    BillingInterval = requestDto.BillingInterval,
                };
                uow.GetRepository<UserSubscription>().Add(subscription);
                _ = await uow.SaveChangesAsync();

                await CreateQuotasAsync(uow, requestDto.SubscriptionPlanId, requestDto.BillingInterval, subscription.Id);

                return new(OperationResult.Succeeded) { Data = subscription.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = 0, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ExtendSubscriptionAsync(long userSubscriptionId, int days)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<UserSubscription>();

                var sub = await repository.GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .Select(t => new { t.ExpirationDate })
                    .FirstOrDefaultAsync();
                if (sub is null)
                {
                    return new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["UserSubscriptionNotFound"] },] };
                }

                // Extends from the subscription's own current ExpirationDate, not "now" - same anchoring as RenewSubscriptionAsync.
                var newExpirationDate = (sub.ExpirationDate ?? DateTimeOffset.UtcNow).AddDays(days);

                var affected = await repository.GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.ExpirationDate, newExpirationDate));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ApplyPlanSwitchAsync(long userSubscriptionId, long newSubscriptionPlanId, decimal newPricePaid)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                // A switch never changes BillingInterval, only SubscriptionPlanId/PricePaid - read the
                // subscription's own existing interval to snapshot quota at the right one below.
                var sub = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .Select(t => new { t.BillingInterval })
                    .FirstOrDefaultAsync();
                if (sub is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.SubscriptionPlanId, newSubscriptionPlanId)
                        .SetProperty(p => p.PricePaid, newPricePaid)
                        .SetProperty(p => p.PendingSwitchSubscriptionPlanId, (long?)null)
                        .SetProperty(p => p.PendingSwitchPricePaid, (decimal?)null));
                if (affected == 0)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                await CreateQuotasAsync(uow, newSubscriptionPlanId, sub.BillingInterval, userSubscriptionId);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RequestPlanSwitchAsync(long userSubscriptionId, long newSubscriptionPlanId, decimal newPricePaid)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.PendingSwitchSubscriptionPlanId, newSubscriptionPlanId)
                        .SetProperty(p => p.PendingSwitchPricePaid, newPricePaid));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
