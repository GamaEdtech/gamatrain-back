namespace GamaEdtech.Domain.Repositories.Faq
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories;

    [Injectable]
    public interface IFaqCategoryRepository : IBaseRepository<ClassificationNode>
    {
        Task<IReadOnlyCollection<ClassificationNode>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken);
    }
}
