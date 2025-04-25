namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Mappers.FaqMappers;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.Faq;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs;
    using GamaEdtech.Domain.Specification.FaqSpecs;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class FaqDomainService
        (
            IFaqCategoryRepository faqCategoryRepository,
            IFaqRepository faqRepository
        )
        : IFaqDomainService
    {
        public async Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken)
        {
            var faqList = await faqRepository.ListAsync(new GetFaqWithDynamicFilterSpecification(dynamicFilterRequest,
                FaqRelations.FaqCategory), cancellationToken);

            return faqList.MapToResult();
        }

        public async Task<IEnumerable<FaqCategoryResponse>> GetFaqCategoryHierarchyAsync(CancellationToken cancellationToken)
        {
            var categories = await faqCategoryRepository.ListAsyncWithSecondaryLevelCacheAsync(cancellationToken);
            var tree = FaqCategory.BuildHierarchyTree(categories);
            return tree.MapToResult();
        }

        public async Task CreateFaqAsync(IEnumerable<string> faqCategoryTitles, string summaryOfQuestion, string question,
            UploadFileResponse uploadFileResult, CancellationToken cancellationToken)
        {
            var faqCategories = await faqCategoryRepository.ListAsync
                (new GetFaqCategoryWithTitlesSpecification(faqCategoryTitles), cancellationToken);

            if (faqCategories.Count == 0)
            {
                throw new ArgumentException("");
            }

            var faq = Faq.Create(summaryOfQuestion, question, faqCategories);

            if (uploadFileResult is not null && uploadFileResult.FileResults != null
                && uploadFileResult.FileResults.Any())
            {
                faq.AddMedia(
                       uploadFileResult.FileResults.Select
                       (file => Media.Create(file.FileName, file.FileAddress, MediaEntity.Faq, MediaType.Photo, faq.Id,
                   file.ContentType))
                );
            }

            _ = await faqRepository.AddAsync(faq, cancellationToken);
        }

        public async Task CreateFaqCategoryAsync(string? parentCategoryTitle, string title, FaqCategoryType categoryType, CancellationToken cancellationToken)
        {
            FaqCategory newCategory;

            if (parentCategoryTitle != null && !string.IsNullOrEmpty(parentCategoryTitle))
            {
                var parentCategory = await faqCategoryRepository.FirstOrDefaultAsync(
                    new GetFaqCategoryWithTitleSpecification(parentCategoryTitle), cancellationToken)
                    ?? throw new EntryPointNotFoundException();

                newCategory = FaqCategory.Create(title, categoryType, parentCategory);
            }
            else
            {
                newCategory = FaqCategory.Create(title, categoryType, null);
            }
            _ = await faqCategoryRepository.AddAsync(newCategory, cancellationToken);
        }
    }
}
