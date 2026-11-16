namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Taxonomy;

    /// <summary>
    /// Copies gama-api's lookup lists into this backend (<c>LegacyTaxonomyItem</c>), keeping gama-api's ids, so the Types
    /// domain can be answered here. Reads gama-api's public <c>types/list</c> only; never writes to gama-api. Safe to run
    /// again: it updates what changed and marks what gama-api no longer lists as deleted.
    /// </summary>
    [Injectable]
    public interface ILegacyTaxonomySyncService
    {
        Task<ResultData<LegacyTaxonomySyncResultDto>> SyncAsync();
    }
}
