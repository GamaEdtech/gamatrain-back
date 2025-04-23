namespace GamaEdtech.Infrastructure.Repositories.Faq
{
    using EFCoreSecondLevelCacheInterceptor;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.Faq;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs;
    using GamaEdtech.Infrastructure.EntityFramework.Context;

    using Microsoft.EntityFrameworkCore;

    public class FaqCategoryRepository(ApplicationDBContext dbContext) :
        BaseRepository<FaqCategory>(dbContext), IFaqCategoryRepository
    {
        public async Task<IReadOnlyCollection<FaqCategory>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken)
            => (await ApplySpecification(new GetAllFaqCategoriesSpecification())
                .Cacheable().ToListAsync(cancellationToken)).AsReadOnly();
    }
}
