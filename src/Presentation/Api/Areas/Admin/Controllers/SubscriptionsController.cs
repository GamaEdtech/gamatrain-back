namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Subscription;
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
                            Polygon = t.Polygon?.Coordinates.Select(t => new CoordinateViewModel { Latitude = t.Y, Longitude = t.X, }),
                            IsActive = t.IsActive,
                            Highlight = t.Highlight,
                            Prices = t.Prices?.Select(p => new SubscriptionPlanPriceResponseViewModel
                            {
                                Id = p.Id,
                                SubscriptionPlanId = p.SubscriptionPlanId,
                                CountryCode = p.CountryCode,
                                Currency = p.Currency,
                                CurrencySymbol = p.Currency.Symbol,
                                Price = p.Price,
                                BillingInterval = p.BillingInterval,
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
                        Polygon = result.Data.Polygon?.Coordinates.Select(t => new CoordinateViewModel { Latitude = t.Y, Longitude = t.X, }),
                        IsActive = result.Data.IsActive,
                        Highlight = result.Data.Highlight,
                        Prices = result.Data.Prices?.Select(p => new SubscriptionPlanPriceResponseViewModel
                        {
                            Id = p.Id,
                            SubscriptionPlanId = p.SubscriptionPlanId,
                            CountryCode = p.CountryCode,
                            Currency = p.Currency,
                            CurrencySymbol = p.Currency.Symbol,
                            Price = p.Price,
                            BillingInterval = p.BillingInterval,
                        }),
                        FeatureGroups = result.Data.FeatureGroups?.Select(g => new PlanFeatureGroupViewModel
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
                    Polygon = polygon,
                    IsActive = request.IsActive,
                    Highlight = request.Highlight,
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
                    Polygon = polygon,
                    IsActive = request.IsActive,
                    Highlight = request.Highlight,
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

        [HttpGet("features/codes"), Produces<ApiResponse<IEnumerable<string>>>()]
        public IActionResult<IEnumerable<string>> GetFeatureCodes()
        {
            try
            {
                return Ok<IEnumerable<string>>(new() { Data = subscriptionService.Value.GetFeatureCodes() });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<string>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("features"), Produces<ApiResponse<ListDataSource<FeatureResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<FeatureResponseViewModel>>> GetFeatures([NotNull, FromQuery] FeaturesRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GetFeaturesAsync(new() { PagingDto = request.PagingDto });
                return Ok<ListDataSource<FeatureResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new FeatureResponseViewModel
                        {
                            Id = t.Id,
                            Code = t.Code,
                            Name = t.Name,
                            Description = t.Description,
                            IsActive = t.IsActive,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<FeatureResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("features"), Produces<ApiResponse<ManageFeatureResponseViewModel>>()]
        public async Task<IActionResult<ManageFeatureResponseViewModel>> CreateFeature([NotNull] ManageFeatureRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManageFeatureAsync(new()
                {
                    Code = request.Code,
                    Name = request.Name,
                    Description = request.Description,
                    IsActive = request.IsActive,
                });
                return Ok<ManageFeatureResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageFeatureResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("features/{id:int}"), Produces<ApiResponse<ManageFeatureResponseViewModel>>()]
        public async Task<IActionResult<ManageFeatureResponseViewModel>> UpdateFeature([FromRoute] int id, [NotNull, FromBody] ManageFeatureRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManageFeatureAsync(new()
                {
                    Id = id,
                    Code = request.Code,
                    Name = request.Name,
                    Description = request.Description,
                    IsActive = request.IsActive,
                });
                return Ok<ManageFeatureResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageFeatureResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("features/{id:int}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemoveFeature([FromRoute] int id)
        {
            try
            {
                var result = await subscriptionService.Value.RemoveFeatureAsync(new IdEqualsSpecification<Feature, int>(id));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("plans/{id:long}/features"), Produces<ApiResponse<IEnumerable<PlanFeatureGroupViewModel>>>()]
        public async Task<IActionResult<IEnumerable<PlanFeatureGroupViewModel>>> GetPlanFeatures([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.GetPlanFeaturesAsync(id);
                return Ok<IEnumerable<PlanFeatureGroupViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(g => new PlanFeatureGroupViewModel
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
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<PlanFeatureGroupViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("plans/{id:long}/features"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> SetPlanFeatures([FromRoute] long id, [NotNull, FromBody] SetPlanFeaturesRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.SetPlanFeaturesAsync(new()
                {
                    SubscriptionPlanId = id,
                    FeatureGroups = (request.FeatureGroups ?? []).Select(t => new PlanFeatureGroupItemDto
                    {
                        FeatureIds = t.FeatureIds ?? [],
                        Limit = t.Limit,
                        Description = t.Description,
                    }),
                });
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("prices"), Produces<ApiResponse<ListDataSource<SubscriptionPlanPriceResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SubscriptionPlanPriceResponseViewModel>>> GetPlanPrices([NotNull, FromQuery] SubscriptionPlanPricesRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GetPlanPricesAsync(new() { PagingDto = request.PagingDto });
                return Ok<ListDataSource<SubscriptionPlanPriceResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new SubscriptionPlanPriceResponseViewModel
                        {
                            Id = t.Id,
                            SubscriptionPlanId = t.SubscriptionPlanId,
                            CountryCode = t.CountryCode,
                            Currency = t.Currency,
                            CurrencySymbol = t.Currency.Symbol,
                            Price = t.Price,
                            BillingInterval = t.BillingInterval,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SubscriptionPlanPriceResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("prices"), Produces<ApiResponse<ManageSubscriptionPlanPriceResponseViewModel>>()]
        public async Task<IActionResult<ManageSubscriptionPlanPriceResponseViewModel>> CreatePlanPrice([NotNull] ManageSubscriptionPlanPriceRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManagePlanPriceAsync(new()
                {
                    SubscriptionPlanId = request.SubscriptionPlanId!.Value,
                    CountryCode = request.CountryCode,
                    Currency = request.Currency!,
                    Price = request.Price!.Value,
                    BillingInterval = request.BillingInterval!,
                });
                return Ok<ManageSubscriptionPlanPriceResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSubscriptionPlanPriceResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("prices/{id:long}"), Produces<ApiResponse<ManageSubscriptionPlanPriceResponseViewModel>>()]
        public async Task<IActionResult<ManageSubscriptionPlanPriceResponseViewModel>> UpdatePlanPrice([FromRoute] long id, [NotNull, FromBody] ManageSubscriptionPlanPriceRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManagePlanPriceAsync(new()
                {
                    Id = id,
                    SubscriptionPlanId = request.SubscriptionPlanId!.Value,
                    CountryCode = request.CountryCode,
                    Currency = request.Currency!,
                    Price = request.Price!.Value,
                    BillingInterval = request.BillingInterval!,
                });
                return Ok<ManageSubscriptionPlanPriceResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSubscriptionPlanPriceResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("prices/{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemovePlanPrice([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.RemovePlanPriceAsync(new IdEqualsSpecification<SubscriptionPlanPrice, long>(id));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("gateway-mappings"), Produces<ApiResponse<ListDataSource<GatewayMappingResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<GatewayMappingResponseViewModel>>> GetGatewayMappings([NotNull, FromQuery] GatewayMappingsRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GetGatewayMappingsAsync(new() { PagingDto = request.PagingDto });
                return Ok<ListDataSource<GatewayMappingResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new GatewayMappingResponseViewModel
                        {
                            Id = t.Id,
                            SubscriptionPlanPriceId = t.SubscriptionPlanPriceId,
                            Gateway = t.Gateway,
                            ExternalProductId = t.ExternalProductId,
                            ExternalPlanId = t.ExternalPlanId,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<GatewayMappingResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("gateway-mappings"), Produces<ApiResponse<ManageGatewayMappingResponseViewModel>>()]
        public async Task<IActionResult<ManageGatewayMappingResponseViewModel>> CreateGatewayMapping([NotNull] ManageGatewayMappingRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManageGatewayMappingAsync(new()
                {
                    SubscriptionPlanPriceId = request.SubscriptionPlanPriceId!.Value,
                    Gateway = request.Gateway!,
                    ExternalProductId = request.ExternalProductId!,
                    ExternalPlanId = request.ExternalPlanId,
                });
                return Ok<ManageGatewayMappingResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageGatewayMappingResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("gateway-mappings/{id:long}"), Produces<ApiResponse<ManageGatewayMappingResponseViewModel>>()]
        public async Task<IActionResult<ManageGatewayMappingResponseViewModel>> UpdateGatewayMapping([FromRoute] long id, [NotNull, FromBody] ManageGatewayMappingRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ManageGatewayMappingAsync(new()
                {
                    Id = id,
                    SubscriptionPlanPriceId = request.SubscriptionPlanPriceId!.Value,
                    Gateway = request.Gateway!,
                    ExternalProductId = request.ExternalProductId!,
                    ExternalPlanId = request.ExternalPlanId,
                });
                return Ok<ManageGatewayMappingResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageGatewayMappingResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("gateway-mappings/{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemoveGatewayMapping([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.RemoveGatewayMappingAsync(new IdEqualsSpecification<SubscriptionPlanGatewayMapping, long>(id));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
