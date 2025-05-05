namespace GamaEdtech.Domain.Services.ClassificationNodeServices
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IClassificationNodeDomainService
    {
        Task CreateClassificationNodeAsync(string[]? parentCategoryTitles, string title, ClassificationNodeType categoryType, CancellationToken cancellationToken);
        Task<IEnumerable<ClassificationNodeResponse>> GetFClassificationNodesHierarchyAsync(CancellationToken cancellationToken);
        Task<ClassificationNodeResponse> GetClassificationNodeAsync(Guid classificationNodeId, CancellationToken cancellationToken);
    }
}
