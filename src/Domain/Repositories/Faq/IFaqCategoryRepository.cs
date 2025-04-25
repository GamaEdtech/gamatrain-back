namespace GamaEdtech.Domain.Repositories.Faq
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories;

    [Injectable]
    public interface IFaqCategoryRepository : IBaseRepository<FaqCategory>
    {
        Task<IReadOnlyCollection<FaqCategory>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken);
    }
}
