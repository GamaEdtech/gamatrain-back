namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Location;
    using GamaEdtech.Presentation.ViewModel.Location;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    [AllowAnonymous]
    public class LocationsController(Lazy<ILogger<LocationsController>> logger, Lazy<ILocationService> locationService) : ApiControllerBase<LocationsController>(logger)
    {
        [HttpGet("search"), Produces(typeof(ApiResponse<IEnumerable<LocationSearchResponseViewModel>>))]
        // The same names for everyone: the edge answers repeats.
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 3600)]
        public async Task<IActionResult<IEnumerable<LocationSearchResponseViewModel>>> SearchLocations([FromQuery] string? query, [FromQuery] int size = 10)
        {
            try
            {
                var result = await locationService.Value.SearchLocationsAsync(query ?? string.Empty, size);
                return Ok<IEnumerable<LocationSearchResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new LocationSearchResponseViewModel
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Type = t.LocationType?.Name.ToLowerInvariant(),
                        StateId = t.StateId,
                        StateTitle = t.StateTitle,
                        CountryId = t.CountryId,
                        CountryTitle = t.CountryTitle,
                    }).ToList(),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<LocationSearchResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("countries"), Produces(typeof(ApiResponse<ListDataSource<LocationsResponseViewModel>>))]
        public async Task<IActionResult<ListDataSource<LocationsResponseViewModel>>> GetCountries([NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.Country),
                });
                return Ok<ListDataSource<LocationsResponseViewModel>>(new(result.Errors)
                {
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

                return Ok<ListDataSource<LocationsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("states/{countryId}"), Produces(typeof(ApiResponse<ListDataSource<LocationsResponseViewModel>>))]
        public async Task<IActionResult<ListDataSource<LocationsResponseViewModel>>> GetStates([NotNull, FromRoute] int countryId, [NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.State).And(new ParentIdEqualsSpecification(countryId)),
                });
                return Ok<ListDataSource<LocationsResponseViewModel>>(new(result.Errors)
                {
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

                return Ok<ListDataSource<LocationsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("cities/{stateId}"), Produces(typeof(ApiResponse<ListDataSource<LocationsResponseViewModel>>))]
        public async Task<IActionResult<ListDataSource<LocationsResponseViewModel>>> GetCities([NotNull, FromRoute] int stateId, [NotNull, FromQuery] LocationsRequestViewModel request)
        {
            try
            {
                var result = await locationService.Value.GetLocationsAsync(new ListRequestDto<Location>
                {
                    PagingDto = request.PagingDto,
                    Specification = new LocationTypeEqualsSpecification(LocationType.City).And(new ParentIdEqualsSpecification(stateId)),
                });
                return Ok<ListDataSource<LocationsResponseViewModel>>(new(result.Errors)
                {
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

                return Ok<ListDataSource<LocationsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
