namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;

    public interface IFaqManager
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterDto getFaqWithDynamicFilterDTO, CancellationToken cancellationToken);
        Task<IEnumerable<FaqCategoryResponse>> GetFaqCategoryHierarchyAsync(CancellationToken cancellationToken);
        Task CreateFaqCategoryAsync(CreateFaqCategoryDto createFaqCategoryDTO, CancellationToken cancellationToken);
        Task AddForumAsync(CreateForumDto createForumDTO, CancellationToken cancellationToken);
    }
}
