namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public interface IFaqDomainService
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken);
        Task<IEnumerable<FaqCategoryResponse>> GetFaqCategoryHierarchyAsync(CancellationToken cancellationToken);
        Task CreateFaqCategoryAsync(string? parentCategoryTitle, string title, FaqCategoryType categoryType, CancellationToken cancellationToken);
    }
}
