namespace GamaEdtech.Domain.Services.ClassificationNodeServices
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Mappers.FaqMappers;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.ClassificationNodes;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ClassificationNodeDomainService(IClassificationNodeRepository classificationNodeRepository) : IClassificationNodeDomainService
    {
        public async Task<IEnumerable<ClassificationNodeResponse>> GetFClassificationNodesHierarchyAsync(CancellationToken cancellationToken)
        {
            var categories = await classificationNodeRepository.ListAsyncWithSecondaryLevelCacheAsync(cancellationToken);
            var tree = ClassificationNode.BuildHierarchyTree(categories);
            return tree.MapToResult();
        }
        public async Task CreateClassificationNodeAsync(string[]? parentCategoryTitles, string title, ClassificationNodeType categoryType, CancellationToken cancellationToken)
        {
            ClassificationNode newCategory;

            if (parentCategoryTitles != null && parentCategoryTitles.Length > 0)
            {
                var parentCategory = await classificationNodeRepository.ListAsync(
                    new GetClassificationNodeWithTitleSpecification(parentCategoryTitles), cancellationToken)
                    ?? throw new EntryPointNotFoundException();

                newCategory = ClassificationNode.Create(title, categoryType, parentCategory);
            }
            else
            {
                newCategory = ClassificationNode.Create(title, categoryType, null);
            }
            _ = await classificationNodeRepository.AddAsync(newCategory, cancellationToken);
        }
    }
}
