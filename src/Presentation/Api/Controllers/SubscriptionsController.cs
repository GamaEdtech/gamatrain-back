namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Specification.Subscription;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Hangfire;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class SubscriptionsController(Lazy<ILogger<SubscriptionsController>> logger, Lazy<ISubscriptionService> subscriptionService
        , Lazy<ISubscriptionQuotaService> subscriptionQuotaService)
        : ApiControllerBase<SubscriptionsController>(logger)
    {
        [HttpGet("plans"), Produces<ApiResponse<SubscriptionPlansResponseViewModel>>()]
        public async Task<IActionResult<SubscriptionPlansResponseViewModel>> GetSubscriptionsList()
        {
            try
            {
                var result = await subscriptionService.Value.GetSubscriptionPlansAsync(new()
                {
                    Specification = new ActiveSpecification(),
                });

                // Regional pricing is currently disabled; the global default rows (null CountryCode) are the resolved prices,
                // one per billing interval the plan is offered at.
                var defaultPricesByPlan = (result.Data.List ?? []).ToDictionary(t => t.Id, t => t.Prices?.Where(p => p.CountryCode is null).ToList());

                var plans = result.Data.List is null
                    ? []
                    : result.Data.List.Select(t => new ActiveSubscriptionPlanResponseViewModel
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Highlight = t.Highlight,
                        Prices = defaultPricesByPlan[t.Id]?.Select(p => new ActiveSubscriptionPlanPriceViewModel
                        {
                            BillingInterval = p.BillingInterval,
                            Currency = p.Currency,
                            CurrencySymbol = p.Currency.Symbol,
                            Price = p.Price,
                        }),
                        FeatureGroups = t.FeatureGroups?.Select(g => new PlanFeatureGroupViewModel
                        {
                            Features = g.Features.Select(f => new PlanFeatureViewModel
                            {
                                FeatureId = f.FeatureId,
                                FeatureCode = f.FeatureCode,
                                FeatureName = f.FeatureName,
                            }),
                            Limit = g.Limit,
                            Description = g.Description,
                        }),
                    });

                // Ready-made tab/period manifest: distinct intervals present anywhere above, in interval order,
                // so the caller doesn't have to scan every plan's prices to know which periods exist.
                var availableBillingIntervals = defaultPricesByPlan.Values
                    .SelectMany(prices => prices ?? [])
                    .Select(p => p.BillingInterval)
                    .Distinct()
                    .OrderBy(bi => bi.Value)
                    .Select(bi => bi.Name)
                    .ToList();

                return Ok<SubscriptionPlansResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Plans = plans, AvailableBillingIntervals = availableBillingIntervals },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SubscriptionPlansResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("plans/{id:long}/purchase"), Produces<ApiResponse<PurchaseSubscriptionResponseViewModel>>()]
        public async Task<IActionResult<PurchaseSubscriptionResponseViewModel>> PurchaseSubscription([FromRoute] long id, [NotNull][FromBody] PurchaseSubscriptionRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.PurchaseSubscriptionAsync(new()
                {
                    UserId = User.UserId(),
                    SubscriptionPlanId = id,
                    BillingInterval = request.BillingInterval!,
                    Gateway = request.Gateway!,
                });

                return Ok<PurchaseSubscriptionResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        UserSubscriptionId = result.Data.UserSubscriptionId,
                        PaymentId = result.Data.PaymentId,
                        Url = result.Data.Url,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PurchaseSubscriptionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("me"), Produces<ApiResponse<UserSubscriptionResponseViewModel>>()]
        public async Task<IActionResult<UserSubscriptionResponseViewModel>> GetCurrentSubscription()
        {
            try
            {
                var result = await subscriptionQuotaService.Value.GetCurrentSubscriptionAsync(User.UserId());

                return Ok<UserSubscriptionResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        SubscriptionPlanId = result.Data.SubscriptionPlanId,
                        PlanTitle = result.Data.PlanTitle,
                        Status = result.Data.Status,
                        StartDate = result.Data.StartDate,
                        ExpirationDate = result.Data.ExpirationDate,
                        PricePaid = result.Data.PricePaid,
                        Currency = result.Data.Currency,
                        BillingInterval = result.Data.BillingInterval,
                        AutoRenews = result.Data.AutoRenews,
                        CancelAtPeriodEnd = result.Data.CancelAtPeriodEnd,
                        PendingSwitchPlanId = result.Data.PendingSwitchPlanId,
                        PendingSwitchPlanTitle = result.Data.PendingSwitchPlanTitle,
                        FeatureGroups = result.Data.FeatureGroups?.Select(t => new UserSubscriptionQuotaViewModel
                        {
                            Features = t.Features.Select(f => new UserSubscriptionQuotaFeatureViewModel
                            {
                                FeatureCode = f.FeatureCode,
                                FeatureName = f.FeatureName,
                                Description = f.Description,
                            }),
                            Limit = t.Limit,
                            Used = t.Used,
                            Remaining = t.Remaining,
                            Description = t.Description,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<UserSubscriptionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>
        /// Cancel-at-period-end for the caller's own current active subscription - quota stays usable until
        /// ExpirationDate. NotValid/SubscriptionNotRecurring for a one-time/GamaTrain subscription, which was
        /// never going to renew.
        /// </summary>
        [HttpPost("me/cancel"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> CancelSubscription()
        {
            try
            {
                var result = await subscriptionService.Value.CancelSubscriptionAsync(User.UserId());
                if (result.Data?.EmailNotification is not null)
                {
                    _ = BackgroundJob.Enqueue<ISubscriptionService>(t => t.SendSubscriptionCancelledEmailAsync(result.Data.EmailNotification));
                }

                return Ok<bool>(new(result.Errors) { Data = result.Data?.Success ?? false });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Reverses a pending cancel-at-period-end request for the caller's own current active subscription - back to renewing normally. Idempotent no-op if nothing was pending.</summary>
        [HttpPost("me/resume"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> ResumeSubscription()
        {
            try
            {
                var result = await subscriptionService.Value.ResumeSubscriptionAsync(User.UserId());
                if (result.Data?.EmailNotification is not null)
                {
                    _ = BackgroundJob.Enqueue<ISubscriptionService>(t => t.SendSubscriptionResumedEmailAsync(result.Data.EmailNotification));
                }

                return Ok<bool>(new(result.Errors) { Data = result.Data?.Success ?? false });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>
        /// Switches the caller's own current active subscription to a different plan (Stripe-recurring only,
        /// same NotValid/SubscriptionNotRecurring as me/cancel for a one-time/GamaTrain subscription). An upgrade
        /// applies immediately with a prorated invoice; a downgrade is deferred to the current period's end.
        /// </summary>
        [HttpPost("me/switch"), Produces<ApiResponse<SwitchSubscriptionPlanResponseViewModel>>()]
        public async Task<IActionResult<SwitchSubscriptionPlanResponseViewModel>> SwitchSubscriptionPlan([NotNull] SwitchSubscriptionPlanRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.SwitchSubscriptionPlanAsync(new()
                {
                    UserId = User.UserId(),
                    SubscriptionPlanId = request.SubscriptionPlanId!.Value,
                });
                if (result.Data?.EmailNotification is not null)
                {
                    _ = BackgroundJob.Enqueue<ISubscriptionService>(t => t.SendSubscriptionSwitchedEmailAsync(result.Data.EmailNotification));
                }

                return Ok<SwitchSubscriptionPlanResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Success = result.Data?.Success ?? false,
                        Immediate = result.Data?.Immediate ?? false,
                        EffectiveDate = result.Data?.EffectiveDate,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SwitchSubscriptionPlanResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
