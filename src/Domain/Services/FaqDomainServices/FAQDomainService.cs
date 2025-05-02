namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core.Extensions;
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
        public async Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync([NotNull] GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken)
        {
            var faqList = await faqRepository.ListAsync(new GetFaqWithDynamicFilterSpecification(dynamicFilterRequest,
                FaqRelations.ClassificationNodeRelationships, FaqRelations.Media), cancellationToken);

            return faqList.MapToResult(dynamicFilterRequest.CustomDateFormat);
        }

        public async Task<IEnumerable<ClassificationNodeResponse>> GetFaqCategoryHierarchyAsync(CustomDateFormat customDateFormat, CancellationToken cancellationToken)
        {
            var categories = await faqCategoryRepository.ListAsyncWithSecondaryLevelCacheAsync(cancellationToken);
            var tree = ClassificationNode.BuildHierarchyTree(categories);
            return tree.MapToResult(customDateFormat);
        }

        public async Task<FaqResponse> CreateFaqAsync(IEnumerable<string> faqCategoryTitles, string summaryOfQuestion, string question,
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
            return faq.MapToResult(CustomDateFormat.ToSolarDate);
        }

        public async Task CreateFaqCategoryAsync(string[]? parentCategoryTitles, string title, ClassificationNodeType categoryType, CancellationToken cancellationToken)
        {
            ClassificationNode newCategory;

            if (parentCategoryTitles != null && parentCategoryTitles.Length > 0)
            {
                var parentCategory = await faqCategoryRepository.ListAsync(
                    new GetFaqCategoryWithTitleSpecification(parentCategoryTitles), cancellationToken)
                    ?? throw new EntryPointNotFoundException();

                newCategory = ClassificationNode.Create(title, categoryType, parentCategory);
            }
            else
            {
                newCategory = ClassificationNode.Create(title, categoryType, null);
            }
            _ = await faqCategoryRepository.AddAsync(newCategory, cancellationToken);
        }
    }
}
