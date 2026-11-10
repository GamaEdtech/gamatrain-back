namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;

    using GamaEdtech.Data.Dto.Location;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Location;
    using GamaEdtech.Presentation.ViewModel.Location;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class LocationsController(Lazy<ILogger<LocationsController>> logger, Lazy<ILocationService> locationService)
        : ApiControllerBase<LocationsController>(logger)
    {
        #region Country

        [HttpGet("countries"), Produces<ApiResponse<ListDataSource<LocationsResponseViewModel>>>()]
        public async Task<IActionResult> GetCountries([NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.Country),
                });
                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>>
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new LocationsResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Code = t.Code,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("countries/{id:int}"), Produces<ApiResponse<LocationResponseViewModel>>()]
        public async Task<IActionResult> GetCountry([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.GetLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<LocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Title = result.Data.Title,
                        Code = result.Data.Code,
                        ParentId = result.Data.ParentId,
                        ParentTitle = result.Data.ParentTitle,
                        LocalTitle = result.Data.LocalTitle,
                        Latitude = result.Data.Coordinates?.Y,
                        Longitude = result.Data.Coordinates?.X,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<LocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("countries"), Produces<ApiResponse<ManageLocationResponseViewModel>>()]
        public async Task<IActionResult> CreateCountry([NotNull] ManageLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Title = request.Title,
                    Code = request.Code,
                    ParentId = request.ParentId,
                    LocationType = LocationType.Country,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("countries/{id:int}"), Produces<ApiResponse<ManageLocationResponseViewModel>>()]
        public async Task<IActionResult> UpdateCountry([FromRoute] int id, [NotNull, FromBody] UpdateLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Id = id,
                    Code = request.Code,
                    Title = request.Title,
                    ParentId = request.ParentId,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("countries/{id:int}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RemoveCountry([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.RemoveLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #endregion

        #region State

        [HttpGet("states"), Produces<ApiResponse<ListDataSource<LocationsResponseViewModel>>>()]
        public async Task<IActionResult> GetStates([NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.State),
                });
                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>>
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new LocationsResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Code = t.Code,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("states/{id:int}"), Produces<ApiResponse<LocationResponseViewModel>>()]
        public async Task<IActionResult> GetState([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.GetLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<LocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Title = result.Data.Title,
                        Code = result.Data.Code,
                        ParentId = result.Data.ParentId,
                        ParentTitle = result.Data.ParentTitle,
                        LocalTitle = result.Data.LocalTitle,
                        Latitude = result.Data.Coordinates?.Y,
                        Longitude = result.Data.Coordinates?.X,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<LocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("states"), Produces<ApiResponse<ManageLocationResponseViewModel>>()]
        public async Task<IActionResult> CreateState([NotNull] ManageLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Title = request.Title,
                    Code = request.Code,
                    ParentId = request.ParentId,
                    LocationType = LocationType.State,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("states/{id:int}"), Produces<ApiResponse<Void>>()]
        public async Task<IActionResult> UpdateState([FromRoute] int id, [NotNull, FromBody] UpdateLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Id = id,
                    Code = request.Code,
                    Title = request.Title,
                    ParentId = request.ParentId,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("states/{id:int}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RemoveState([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.RemoveLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #endregion

        #region City

        [HttpGet("cities"), Produces<ApiResponse<ListDataSource<LocationsResponseViewModel>>>()]
        public async Task<IActionResult> GetCities([NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.City),
                });
                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>>
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new LocationsResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Code = t.Code,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ListDataSource<LocationsResponseViewModel>> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("cities/{id:int}"), Produces<ApiResponse<LocationResponseViewModel>>()]
        public async Task<IActionResult> GetCity([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.GetLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<LocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Title = result.Data.Title,
                        Code = result.Data.Code,
                        ParentId = result.Data.ParentId,
                        ParentTitle = result.Data.ParentTitle,
                        LocalTitle = result.Data.LocalTitle,
                        Latitude = result.Data.Coordinates?.Y,
                        Longitude = result.Data.Coordinates?.X,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<LocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("cities"), Produces<ApiResponse<ManageLocationResponseViewModel>>()]
        public async Task<IActionResult> CreateCity([NotNull] ManageLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Title = request.Title,
                    Code = request.Code,
                    ParentId = request.ParentId,
                    LocationType = LocationType.City,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("cities/{id:int}"), Produces<ApiResponse<ManageLocationResponseViewModel>>()]
        public async Task<IActionResult> UpdateCity([FromRoute] int id, [NotNull, FromBody] UpdateLocationRequestViewModel request)
        {
            try
            {
                NetTopologySuite.Geometries.Point? coordinates = null;
                if (request.Latitude.HasValue && request.Longitude.HasValue)
                {
                    var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    coordinates = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(request.Longitude.Value, request.Latitude.Value));
                }

                var result = await locationService.Value.ManageLocationAsync(new ManageLocationRequestDto
                {
                    Id = id,
                    Code = request.Code,
                    Title = request.Title,
                    ParentId = request.ParentId,
                    LocalTitle = request.LocalTitle,
                    Coordinates = coordinates,
                });
                return Ok(new ApiResponse<ManageLocationResponseViewModel>
                {
                    Errors = result.Errors,
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ManageLocationResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("cities/{id:int}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RemoveCity([FromRoute] int id)
        {
            try
            {
                var result = await locationService.Value.RemoveLocationAsync(new IdEqualsSpecification<Location, int>(id));
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #endregion
    }
}
