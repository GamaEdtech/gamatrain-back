namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    [Injectable]
    public interface IFaqDomainService
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken);
        Task<FaqResponse> CreateFaqAsync(IEnumerable<string> classificationNodes, string summaryOfQuestion, string question,
            UploadFileResponse uploadFileResult, CancellationToken cancellationToken);
        Task AddFaqRelationShipAsync(IEnumerable<Guid> classificationNodeIds, Guid faqId, CancellationToken cancellationToken);
    }
}
