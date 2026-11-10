namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;

    using GamaEdtech.Data.Dto.Location;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface ILocationService
    {
        Task<ResultData<ListDataSource<LocationsDto>>> GetLocationsAsync(ListRequestDto<Location>? requestDto = null);
        Task<ResultData<List<KeyValuePair<int, string?>>>> GetTitlesAsync([NotNull] ISpecification<Location> specification);
        Task<ResultData<List<LocationSearchResultDto>>> SearchLocationsAsync([NotNull] string query, int size);
        Task<ResultData<LocationDto>> GetLocationAsync([NotNull] ISpecification<Location> specification);
        Task<ResultData<int>> ManageLocationAsync([NotNull] ManageLocationRequestDto requestDto);
        Task<ResultData<bool>> RemoveLocationAsync([NotNull] ISpecification<Location> specification);
    }
}
