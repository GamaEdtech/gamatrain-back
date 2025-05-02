namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IFaqDomainService
    {
        Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken);
        Task<IEnumerable<ClassificationNodeResponse>> GetFaqCategoryHierarchyAsync(CustomDateFormat customDateFormat, CancellationToken cancellationToken);
        Task CreateFaqCategoryAsync(string[]? parentCategoryTitles, string title, ClassificationNodeType categoryType, CancellationToken cancellationToken);
        Task<FaqResponse> CreateFaqAsync(IEnumerable<string> faqCategoryTitles, string summaryOfQuestion, string question,
            UploadFileResponse uploadFileResult, CancellationToken cancellationToken);
    }
}
