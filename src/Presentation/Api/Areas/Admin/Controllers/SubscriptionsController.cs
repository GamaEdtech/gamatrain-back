namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Microsoft.AspNetCore.Mvc;

    using NetTopologySuite;
    using NetTopologySuite.Geometries;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class SubscriptionsController(Lazy<ILogger<SubscriptionsController>> logger, Lazy<ISubscriptionService> subscriptionService)
        : ApiControllerBase<SubscriptionsController>(logger)
    {
        [HttpGet("plans"), Produces<ApiResponse<ListDataSource<SubscriptionPlanResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SubscriptionPlanResponseViewModel>>> GetSubscriptions([NotNull, FromQuery] SubscriptionPlansRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GetSubscriptionPlansAsync(new()
                {
                    PagingDto = request.PagingDto,
                });

                return Ok<ListDataSource<SubscriptionPlanResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new SubscriptionPlanResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Currency = t.Currency,
                            Price = t.Price,
                            Polygon = t.Polygon?.Coordinates.Select(t => new CoordinateViewModel { Latitude = t.Y, Longitude = t.X, }),
                            Point = t.Point,
                            IsActive = t.IsActive,
                            Highlight = t.Highlight,
                            BillingInterval = t.BillingInterval,
                            CurrencySymbol = t.Currency.Symbol,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SubscriptionPlanResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("plans/{id:long}"), Produces<ApiResponse<SubscriptionPlanResponseViewModel>>()]
        public async Task<IActionResult<SubscriptionPlanResponseViewModel>> GetSubscriptionPlan([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.GetSubscriptionPlanAsync(new IdEqualsSpecification<SubscriptionPlan, long>(id));
                return Ok<SubscriptionPlanResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Title = result.Data.Title,
                        Currency = result.Data.Currency,
                        Price = result.Data.Price,
                        Polygon = result.Data.Polygon?.Coordinates.Select(t => new CoordinateViewModel { Latitude = t.Y, Longitude = t.X, }),
                        Point = result.Data.Point,
                        IsActive = result.Data.IsActive,
                        Highlight = result.Data.Highlight,
                        BillingInterval = result.Data.BillingInterval,
                        CurrencySymbol = result.Data.Currency.Symbol,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SubscriptionPlanResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("plans"), Produces<ApiResponse<ManageSubscriptionPlanResponseViewModel>>()]
        public async Task<IActionResult<ManageSubscriptionPlanResponseViewModel>> CreateSubscriptionPlan([NotNull] ManageSubscriptionPlanRequestViewModel request)
        {
            try
            {
                Polygon? polygon = null;
                if (request.Polygon is not null)
                {
                    var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    polygon = geometryFactory.CreatePolygon([.. request.Polygon.Select(t => new Coordinate(t.Latitude.GetValueOrDefault(), t.Longitude.GetValueOrDefault()))]);
                }

                var result = await subscriptionService.Value.ManageSubscriptionPlanAsync(new()
                {
                    Title = request.Title,
                    Currency = request.Currency,
                    Price = request.Price,
                    Polygon = polygon,
                    Point = request.Point,
                    IsActive = request.IsActive,
                    Highlight = request.Highlight,
                    BillingInterval = request.BillingInterval,
                });
                return Ok<ManageSubscriptionPlanResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSubscriptionPlanResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("plans/{id:long}"), Produces<ApiResponse<ManageSubscriptionPlanResponseViewModel>>()]
        public async Task<IActionResult<ManageSubscriptionPlanResponseViewModel>> UpdateSubscriptionPlan([FromRoute] long id, [NotNull, FromBody] ManageSubscriptionPlanRequestViewModel request)
        {
            try
            {
                Polygon? polygon = null;
                if (request.Polygon is not null)
                {
                    var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    polygon = geometryFactory.CreatePolygon([.. request.Polygon.Select(t => new Coordinate(t.Latitude.GetValueOrDefault(), t.Longitude.GetValueOrDefault()))]);
                }

                var result = await subscriptionService.Value.ManageSubscriptionPlanAsync(new()
                {
                    Id = id,
                    Title = request.Title,
                    Currency = request.Currency,
                    Price = request.Price,
                    Polygon = polygon,
                    Point = request.Point,
                    IsActive = request.IsActive,
                    Highlight = request.Highlight,
                    BillingInterval = request.BillingInterval,
                });
                return Ok<ManageSubscriptionPlanResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSubscriptionPlanResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("plans/{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemoveSubscriptionPlan([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.RemoveSubscriptionPlanAsync(new IdEqualsSpecification<SubscriptionPlan, long>(id));
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
