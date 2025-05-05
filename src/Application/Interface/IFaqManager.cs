namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ClassificationNodes;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;

    [Injectable]
    public interface IFaqManager
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterDto getFaqWithDynamicFilterDTO, CancellationToken cancellationToken);
        Task<FaqResponse> AddForumAsync(CreateForumDto createForumDTO, CancellationToken cancellationToken);
        Task AddFaqRelationShipAsync(AddClassificationNode addClassificationNode, Guid faqId, CancellationToken cancellationToken);
    }
}
