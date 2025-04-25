namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IFaqDomainService
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken);
        Task<IEnumerable<FaqCategoryResponse>> GetFaqCategoryHierarchyAsync(CancellationToken cancellationToken);
        Task CreateFaqCategoryAsync(string? parentCategoryTitle, string title, FaqCategoryType categoryType, CancellationToken cancellationToken);
        Task CreateFaqAsync(IEnumerable<string> faqCategoryTitles, string summaryOfQuestion, string question,
            UploadFileResponse uploadFileResult, CancellationToken cancellationToken);
    }
}
