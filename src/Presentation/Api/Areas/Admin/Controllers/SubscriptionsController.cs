namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Subscription;
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
                                Limits = g.Limits.Select(l => new PlanFeatureLimitViewModel { BillingInterval = l.BillingInterval, Limit = l.Limit }),
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
                            Limits = g.Limits.Select(l => new PlanFeatureLimitViewModel { BillingInterval = l.BillingInterval, Limit = l.Limit }),
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
                        Limits = g.Limits.Select(l => new PlanFeatureLimitViewModel { BillingInterval = l.BillingInterval, Limit = l.Limit }),
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
                        Limits = (t.Limits ?? []).Where(l => l.BillingInterval is not null)
                            .Select(l => new PlanFeatureLimitDto { BillingInterval = l.BillingInterval!, Limit = l.Limit }),
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
                var result = await subscriptionService.Value.GetPlanPricesAsync(new()
                {
                    PagingDto = request.PagingDto,
                    Specification = request.SubscriptionPlanId.HasValue ? new PlanIdEqualsSpecification(request.SubscriptionPlanId.Value) : null,
                });
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

        /// <summary>Admin visibility: list any user's subscription(s), filterable by userId/status, for support cases.</summary>
        [HttpGet("users"), Produces<ApiResponse<ListDataSource<AdminUserSubscriptionResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<AdminUserSubscriptionResponseViewModel>>> GetUserSubscriptions([NotNull, FromQuery] AdminUserSubscriptionsRequestViewModel request)
        {
            try
            {
                ISpecification<UserSubscription>? specification = null;

                if (request.UserId.HasValue)
                {
                    specification = new UserIdEqualsSpecification<UserSubscription, long>(request.UserId.Value);
                }

                if (request.Status is not null)
                {
                    var spec = new UserSubscriptionStatusEqualsSpecification(request.Status);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await subscriptionService.Value.GetUserSubscriptionsAsync(new ListRequestDto<UserSubscription>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<AdminUserSubscriptionResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(ToViewModel),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<AdminUserSubscriptionResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Admin visibility: a single user subscription's detail, for support cases.</summary>
        [HttpGet("users/{id:long}"), Produces<ApiResponse<AdminUserSubscriptionResponseViewModel>>()]
        public async Task<IActionResult<AdminUserSubscriptionResponseViewModel>> GetUserSubscription([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.GetUserSubscriptionAsync(new IdEqualsSpecification<UserSubscription, long>(id));
                return Ok<AdminUserSubscriptionResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : ToViewModel(result.Data),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<AdminUserSubscriptionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        private static AdminUserSubscriptionResponseViewModel ToViewModel(AdminUserSubscriptionDto t) => new()
        {
            Id = t.Id,
            UserId = t.UserId,
            UserEmail = t.UserEmail,
            SubscriptionPlanId = t.SubscriptionPlanId,
            PlanTitle = t.PlanTitle,
            Status = t.Status,
            CreationDate = t.CreationDate,
            StartDate = t.StartDate,
            ExpirationDate = t.ExpirationDate,
            PricePaid = t.PricePaid,
            Currency = t.Currency,
            BillingInterval = t.BillingInterval,
            AutoRenews = t.AutoRenews,
            CancelAtPeriodEnd = t.CancelAtPeriodEnd,
            PendingSwitchPlanId = t.PendingSwitchPlanId,
            PendingSwitchPlanTitle = t.PendingSwitchPlanTitle,
            PendingSwitchBillingInterval = t.PendingSwitchBillingInterval,
            LastPaymentFailedDate = t.LastPaymentFailedDate,
            ExternalSubscriptionId = t.ExternalSubscriptionId,
            Gateway = t.Gateway,
            FeatureGroups = t.FeatureGroups?.Select(g => new SubscriptionQuotaStatusViewModel
            {
                Features = g.Features.Select(f => new PlanFeatureViewModel
                {
                    FeatureId = f.FeatureId,
                    FeatureCode = f.FeatureCode,
                    FeatureName = f.FeatureName,
                }),
                Limit = g.Limit,
                Used = g.Used,
                Remaining = g.Remaining,
                Description = g.Description,
            }),
        };

        /// <summary>Admin-initiated comped grant for a support case - creates and activates a subscription immediately, bypassing payment.</summary>
        [HttpPost("users/grant"), Produces<ApiResponse<GrantUserSubscriptionResponseViewModel>>()]
        public async Task<IActionResult<GrantUserSubscriptionResponseViewModel>> GrantUserSubscription([NotNull] GrantUserSubscriptionRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GrantSubscriptionAsync(new()
                {
                    UserId = request.UserId!.Value,
                    SubscriptionPlanId = request.SubscriptionPlanId!.Value,
                    BillingInterval = request.BillingInterval!,
                });
                return Ok<GrantUserSubscriptionResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<GrantUserSubscriptionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Admin-initiated immediate revocation for a support case - stops access right away (unlike the user-facing cancel-at-period-end flow), terminating the gateway-side subscription first if it's recurring.</summary>
        [HttpPost("users/{id:long}/revoke"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RevokeUserSubscription([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.RevokeUserSubscriptionAsync(id);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Admin-initiated support-case extension - pushes ExpirationDate forward by the given number of days. Local record only, never re-bills or touches the gateway side.</summary>
        [HttpPost("users/{id:long}/extend"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> ExtendUserSubscription([FromRoute] long id, [NotNull] ExtendUserSubscriptionRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.ExtendUserSubscriptionAsync(id, request.Days!.Value);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>
        /// Admin-initiated "resync from gateway" for a support case - the source-of-truth counterpart to
        /// <c>extend</c>'s blunt day-count guess. Reads the gateway's own live status/current period end and, if
        /// it confirms this subscription is still active, syncs ExpirationDate directly to it and resets quota -
        /// the same self-healing check the nightly ExpireOverdueSubscriptions job does automatically once past
        /// its own grace period, available here immediately for a support case. NotValid/SubscriptionNotRecurring
        /// for a one-time/GamaTrain subscription (use <c>extend</c> instead) -
        /// see docs/business/subscriptions.md, "Reconciling against the gateway before expiring".
        /// </summary>
        [HttpPost("users/{id:long}/resync"), Produces<ApiResponse<ResyncSubscriptionResponseViewModel>>()]
        public async Task<IActionResult<ResyncSubscriptionResponseViewModel>> ResyncUserSubscription([FromRoute] long id)
        {
            try
            {
                var result = await subscriptionService.Value.ResyncUserSubscriptionAsync(id);
                return Ok<ResyncSubscriptionResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Synced = result.Data.Synced,
                        NewExpirationDate = result.Data.NewExpirationDate,
                        GatewayStatus = result.Data.GatewayStatus,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ResyncSubscriptionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Admin visibility: the raw consumption event log (SubscriptionQuotaConsumptionLog), filterable by userId/featureCode/date range, for support cases or auditing.</summary>
        [HttpGet("usage"), Produces<ApiResponse<ListDataSource<SubscriptionUsageEventResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SubscriptionUsageEventResponseViewModel>>> GetUsageHistory([NotNull, FromQuery] SubscriptionUsageHistoryRequestViewModel request)
        {
            try
            {
                ISpecification<SubscriptionQuotaConsumptionLog>? specification = null;

                if (request.UserId.HasValue)
                {
                    specification = new UserIdEqualsSpecification<SubscriptionQuotaConsumptionLog, long>(request.UserId.Value);
                }

                if (!string.IsNullOrWhiteSpace(request.FeatureCode))
                {
                    var spec = new ConsumptionLogFeatureCodeEqualsSpecification(request.FeatureCode);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.IdentifierId.HasValue)
                {
                    var spec = new IdentifierIdEqualsSpecification<SubscriptionQuotaConsumptionLog>(request.IdentifierId.Value);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.FromDate.HasValue || request.ToDate.HasValue)
                {
                    var spec = new ConsumptionLogDateRangeSpecification(request.FromDate, request.ToDate);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await subscriptionService.Value.GetUsageHistoryAsync(new ListRequestDto<SubscriptionQuotaConsumptionLog>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<SubscriptionUsageEventResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new SubscriptionUsageEventResponseViewModel
                        {
                            Id = t.Id,
                            UserId = t.UserId,
                            UserEmail = t.UserEmail,
                            UserSubscriptionId = t.UserSubscriptionId,
                            SubscriptionPlanId = t.SubscriptionPlanId,
                            PlanTitle = t.PlanTitle,
                            FeatureId = t.FeatureId,
                            FeatureCode = t.FeatureCode,
                            FeatureName = t.FeatureName,
                            Amount = t.Amount,
                            IdentifierId = t.IdentifierId,
                            CreationDate = t.CreationDate,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SubscriptionUsageEventResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Admin visibility: per-feature usage totals for a date range - scoped to one user when userId is supplied, a global aggregate dashboard otherwise.</summary>
        [HttpGet("usage/aggregate"), Produces<ApiResponse<IEnumerable<SubscriptionUsageAggregateResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<SubscriptionUsageAggregateResponseViewModel>>> GetUsageAggregate([NotNull, FromQuery] SubscriptionUsageAggregateRequestViewModel request)
        {
            try
            {
                var result = await subscriptionService.Value.GetUsageAggregateAsync(new()
                {
                    UserId = request.UserId,
                    FromDate = request.FromDate!.Value,
                    ToDate = request.ToDate!.Value,
                });
                return Ok<IEnumerable<SubscriptionUsageAggregateResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new SubscriptionUsageAggregateResponseViewModel
                    {
                        FeatureId = t.FeatureId,
                        FeatureCode = t.FeatureCode,
                        FeatureName = t.FeatureName,
                        TotalAmount = t.TotalAmount,
                        EventCount = t.EventCount,
                        DistinctUserCount = t.DistinctUserCount,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<SubscriptionUsageAggregateResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
