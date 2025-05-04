namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ClassificationNodes;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Services.ClassificationNodeServices;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ClassificationNodeService(IClassificationNodeDomainService classificationNodeDomainService) : IClassificationNodeService
    {

        public Task CreateClassificationNodeAsync([NotNull] CreateClassificationNodeDto createFaqCategoryDTO, CancellationToken cancellationToken)
            => classificationNodeDomainService.CreateClassificationNodeAsync(createFaqCategoryDTO.ParentCategoryTitles, createFaqCategoryDTO.Title,
        createFaqCategoryDTO.FaqCategoryType, cancellationToken);

        public Task<IEnumerable<ClassificationNodeResponse>> GetClassificationNodesHierarchyAsync(CancellationToken cancellationToken)
            => classificationNodeDomainService.GetFClassificationNodesHierarchyAsync(cancellationToken);
    }
}
