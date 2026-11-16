namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Taxonomy;

    /// <summary>
    /// Copies gama-api's lookup lists into this backend (<c>LegacyTaxonomyItem</c>), keeping gama-api's ids, so the Types
    /// domain and the past paper page can be answered here. Reads gama-api's <c>type_slave</c> table read-only (live and
    /// deleted items, with their metadata); never writes to gama-api. Safe to run again: it updates what changed and marks
    /// what gama-api removed as deleted. <c>NotValid</c> without <c>Migration:LegacyDatabase:ConnectionString</c>.
    /// </summary>
    [Injectable]
    public interface ILegacyTaxonomySyncService
    {
        Task<ResultData<LegacyTaxonomySyncResultDto>> SyncAsync();
    }
}
