namespace GamaEdtech.Domain.Repositories.Faq
{
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories;

    public interface IFaqCategoryRepository : IBaseRepository<FaqCategory>
    {
        Task<IReadOnlyCollection<FaqCategory>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken);
    }
}
