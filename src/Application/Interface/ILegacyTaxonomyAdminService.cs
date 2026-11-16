namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Taxonomy;

    /// <summary>
    /// The admin panel's lookup lists (boards, grades, subjects, courses, classifications, topics, papers, extra file types,
    /// variants) in this backend's copy. Reading works at any time; adding, editing and deleting only once
    /// <c>Migration:WriteRoutes:Types</c> is <c>Native</c> (then the copy from gama-api no longer runs).
    /// </summary>
    [Injectable]
    public interface ILegacyTaxonomyAdminService
    {
        /// <summary>A list's items in list order, under <paramref name="parentKey"/> when given, with
        /// <paramref name="title"/> in the title when given.</summary>
        Task<ResultData<TaxonomyItemListDto>> GetItemsAsync(short kind, string? parentKey, string? title, bool includeDeleted, int page, int pageSize);

        Task<ResultData<TaxonomyItemDto>> GetItemAsync(int id);

        /// <summary>Adds an item under the next id after the highest.</summary>
        Task<ResultData<int>> CreateItemAsync([NotNull] TaxonomyItemEditDto requestDto);

        Task<ResultData<bool>> UpdateItemAsync(int id, [NotNull] TaxonomyItemEditDto requestDto);

        /// <summary>Hides an item (gama-api's <c>bit_delete</c>); papers that use it keep it.</summary>
        Task<ResultData<bool>> RemoveItemAsync(int id);
    }
}
