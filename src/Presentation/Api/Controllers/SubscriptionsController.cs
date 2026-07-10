namespace GamaEdtech.Presentation.Api.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Specification.Subscription;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class SubscriptionsController(Lazy<ILogger<SubscriptionsController>> logger, Lazy<ISubscriptionService> subscriptionService, Lazy<IIdentityService> identityService)
        : ApiControllerBase<SubscriptionsController>(logger)
    {
        [HttpGet("plans"), Produces<ApiResponse<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>> GetSubscriptionsList()
        {
            try
            {
                var coordinate = await identityService.Value.GetUserCoordinateAsync(new IdEqualsSpecification<ApplicationUser, long>(User.UserId()));
                if (coordinate.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>(new(coordinate.Errors));
                }

                var result = await subscriptionService.Value.GetSubscriptionPlansAsync(new()
                {
                    Specification = new ActiveSpecification()
                        .And(new CoordinateInsideSpecification(coordinate.Data)),
                });

                return Ok<IEnumerable<ActiveSubscriptionPlanResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null
                        ? []
                        : result.Data.List.Select(t =>
                        {
                            // Regional pricing is currently disabled; the global default row (null CountryCode) is the resolved price.
                            var defaultPrice = t.Prices?.FirstOrDefault(p => p.CountryCode is null);
                            return new ActiveSubscriptionPlanResponseViewModel
                            {
                                Id = t.Id,
                                Title = t.Title,
                                Currency = defaultPrice?.Currency,
                                Price = defaultPrice?.Price,
                                CurrencySymbol = defaultPrice?.Currency?.Symbol,
                                Highlight = t.Highlight,
                                BillingInterval = t.BillingInterval,
                                Features = t.Features?.Select(f => new PlanFeatureViewModel
                                {
                                    FeatureId = f.FeatureId,
                                    FeatureCode = f.FeatureCode,
                                    FeatureName = f.FeatureName,
                                    Limit = f.Limit,
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
    }
}
