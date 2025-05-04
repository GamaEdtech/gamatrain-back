namespace GamaEdtech.Domain.Repositories.ClassificationNodes
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories;

    [Injectable]
    public interface IClassificationNodeRepository : IBaseRepository<ClassificationNode>
    {
        Task<IReadOnlyCollection<ClassificationNode>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken);
    }
}
