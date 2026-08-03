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

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class SubscriptionsController(Lazy<ILogger<SubscriptionsController>> logger, Lazy<ISubscriptionService> subscriptionService
        , Lazy<ISubscriptionQuotaService> subscriptionQuotaService)
        : ApiControllerBase<SubscriptionsController>(logger)
    {
        [HttpGet("plans"), Produces<ApiResponse<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>> GetSubscriptionsList()
        {
            try
            {
                var result = await subscriptionService.Value.GetSubscriptionPlansAsync(new()
                {
                    Specification = new ActiveSpecification(),
                });

                return Ok<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null
                        ? []
                        : result.Data.List.Select(t =>
                        {
                            // Regional pricing is currently disabled; the global default rows (null CountryCode) are the resolved prices,
                            // one per billing interval the plan is offered at.
                            var defaultPrices = t.Prices?.Where(p => p.CountryCode is null);
                            return new ActiveSubscriptionPlanResponseViewModel
                            {
                                Id = t.Id,
                                Title = t.Title,
                                Highlight = t.Highlight,
                                Prices = defaultPrices?.Select(p => new ActiveSubscriptionPlanPriceViewModel
                                {
                                    BillingInterval = p.BillingInterval,
                                    Currency = p.Currency,
                                    CurrencySymbol = p.Currency.Symbol,
                                    Price = p.Price,
                                }),
                                Features = t.Features?.Select(f => new PlanFeatureViewModel
                                {
                                    FeatureId = f.FeatureId,
                                    FeatureCode = f.FeatureCode,
                                    FeatureName = f.FeatureName,
                                    Limit = f.Limit,
                                    Description = f.Description,
                                    PooledFeatureCodes = f.PooledFeatureCodes,
                                }),
                            };
                        }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
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
                        Quotas = result.Data.Quotas?.Select(t => new UserSubscriptionQuotaViewModel
                        {
                            Features = t.Features.Select(f => new UserSubscriptionQuotaFeatureViewModel
                            {
                                FeatureCode = f.FeatureCode,
                                FeatureName = f.FeatureName,
                            }),
                            Limit = t.Limit,
                            Used = t.Used,
                            Remaining = t.Remaining,
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
    }
}
