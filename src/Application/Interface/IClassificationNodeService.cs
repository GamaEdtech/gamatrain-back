namespace GamaEdtech.Application.Interface
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Tasks;

    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ClassificationNodes;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;

    [Injectable]
    public interface IClassificationNodeService
    {
        Task CreateClassificationNodeAsync([NotNull] CreateClassificationNodeDto createFaqCategoryDTO, CancellationToken cancellationToken);
        Task<IEnumerable<ClassificationNodeResponse>> GetClassificationNodesHierarchyAsync(CancellationToken cancellationToken);
        Task<ClassificationNodeResponse> GetClassificationNodeAsync(Guid classificationNodeId, CancellationToken cancellationToken);
    }
}
