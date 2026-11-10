namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Common.Core.Extensions.Collections.Generic;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Data.Dto.Location;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;
    using GamaEdtech.Application.Interface;

    public class LocationService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<LocationService>> localizer, Lazy<ILogger<LocationService>> logger)
        : LocalizableServiceBase<LocationService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ILocationService
    {
        public async Task<ResultData<ListDataSource<LocationsDto>>> GetLocationsAsync(ListRequestDto<Location>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Location, int>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new LocationsDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Code = t.Code,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<List<KeyValuePair<int, string?>>>> GetTitlesAsync([NotNull] ISpecification<Location> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Location, int>().GetManyQueryable(specification)
                    .Select(t => new KeyValuePair<int, string?>(t.Id, t.Title)).ToListAsync();
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        private const int SearchCandidates = 40;
        private const int MaxSearchLength = 100;

        public async Task<ResultData<List<LocationSearchResultDto>>> SearchLocationsAsync([NotNull] string query, int size)
        {
            try
            {
                // Names are short; a long text is a mistake or abuse, not a place.
                var text = query.Trim();
                if (text.Length > MaxSearchLength)
                {
                    text = text[..MaxSearchLength];
                }

                if (text.Length < 2)
                {
                    return new(OperationResult.Succeeded) { Data = [] };
                }

                // Candidates: names that start with the text first, then bigger places, then shorter names.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var items = await uow.GetRepository<Location, int>()
                    .GetManyQueryable(t => (t.Title != null && t.Title.Contains(text)) || (t.LocalTitle != null && t.LocalTitle.Contains(text)))
                    .OrderByDescending(t => t.Title!.StartsWith(text))
                    .ThenBy(t => t.LocationType)
                    .ThenBy(t => t.Title!.Length)
                    .Take(SearchCandidates)
                    .Select(t => new
                    {
                        t.Id,
                        t.Title,
                        t.LocationType,
                        t.ParentId,
                        ParentTitle = t.Parent != null ? t.Parent.Title : null,
                        GrandParentId = t.Parent != null ? t.Parent.ParentId : null,
                        GrandParentTitle = t.Parent != null && t.Parent.Parent != null ? t.Parent.Parent.Title : null,
                    }).ToListAsync();

                // A city's parent is its state and the state's parent its country; a state's parent is its country.
                List<LocationSearchResultDto> result = [.. items.Select(t => t.GrandParentId.HasValue
                    ? new LocationSearchResultDto
                    {
                        Id = t.Id,
                        Title = t.Title,
                        LocationType = t.LocationType,
                        StateId = t.ParentId,
                        StateTitle = t.ParentTitle,
                        CountryId = t.GrandParentId,
                        CountryTitle = t.GrandParentTitle,
                    }
                    : new LocationSearchResultDto
                    {
                        Id = t.Id,
                        Title = t.Title,
                        LocationType = t.LocationType,
                        CountryId = t.ParentId,
                        CountryTitle = t.ParentTitle,
                    })];

                // Among them, places with schools come first, the most schools first: "dar" should find Dar es Salaam
                // before a same-prefixed place nobody looks for here.
                var ids = result.ConvertAll(t => t.Id);
                var schools = uow.GetRepository<School>().GetManyQueryable();
                var counts = new Dictionary<int, int>();
                foreach (var row in await schools.Where(t => t.CountryId != null && ids.Contains(t.CountryId.Value)).GroupBy(t => t.CountryId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
                {
                    counts[row.Key] = row.Count;
                }

                foreach (var row in await schools.Where(t => t.StateId != null && ids.Contains(t.StateId.Value)).GroupBy(t => t.StateId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
                {
                    counts[row.Key] = row.Count;
                }

                foreach (var row in await schools.Where(t => t.CityId != null && ids.Contains(t.CityId.Value)).GroupBy(t => t.CityId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
                {
                    counts[row.Key] = row.Count;
                }

                result = [.. result
                    .Select((item, index) => (item, index, count: counts.GetValueOrDefault(item.Id)))
                    .OrderByDescending(t => t.item.Title?.StartsWith(text, StringComparison.OrdinalIgnoreCase) == true)
                    .ThenByDescending(t => t.count > 0)
                    .ThenByDescending(t => t.count)
                    .ThenBy(t => t.index)
                    .Take(Math.Clamp(size, 1, 20))
                    .Select(t => t.item)];
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<LocationDto>> GetLocationAsync([NotNull] ISpecification<Location> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var location = await uow.GetRepository<Location, int>().GetManyQueryable(specification).Select(t => new LocationDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Code = t.Code,
                    ParentId = t.ParentId,
                    ParentTitle = t.Parent != null ? t.Parent.Title : null,
                    Coordinates = t.Coordinates,
                    LocalTitle = t.LocalTitle,
                }).FirstOrDefaultAsync();

                return location is null
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["LocationNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = location };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<int>> ManageLocationAsync([NotNull] ManageLocationRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Location, int>();
                Location? location = null;

                if (requestDto.Id.HasValue)
                {
                    location = await repository.GetAsync(requestDto.Id.Value);
                    if (location is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["LocationNotFound"] },],
                        };
                    }

                    location.ParentId = requestDto.ParentId ?? location.ParentId;
                    location.Title = requestDto.Title ?? location.Title;
                    location.Code = requestDto.Code ?? location.Code;
                    location.LocationType = requestDto.LocationType ?? location.LocationType;
                    location.Coordinates = requestDto.Coordinates ?? location.Coordinates;
                    location.LocalTitle = requestDto.LocalTitle ?? location.LocalTitle;

                    _ = repository.Update(location);
                }
                else
                {
                    location = new Location
                    {
                        Title = requestDto.Title,
                        Code = requestDto.Code,
                        LocationType = requestDto.LocationType,
                        ParentId = requestDto.ParentId,
                        Coordinates = requestDto.Coordinates,
                        LocalTitle = requestDto.LocalTitle,
                    };
                    repository.Add(location);
                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = location.Id };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidParentId"], }] };
            }
            catch (UniqueConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["DuplicateError"], }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveLocationAsync([NotNull] ISpecification<Location> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var location = await uow.GetRepository<Location, int>().GetAsync(specification);
                if (location is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["LocationNotFound"] },],
                    };
                }

                uow.GetRepository<Location, int>().Remove(location);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["LocationCantBeRemoved"], },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }
    }
}
