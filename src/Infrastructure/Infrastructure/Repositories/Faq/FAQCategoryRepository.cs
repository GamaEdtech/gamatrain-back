namespace GamaEdtech.Infrastructure.Repositories.Faq
{
    using EFCoreSecondLevelCacheInterceptor;

    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.Faq;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs;
    using GamaEdtech.Infrastructure.EntityFramework.Context;

    using Microsoft.EntityFrameworkCore;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class FaqCategoryRepository(ApplicationDBContext dbContext) :
        BaseRepository<ClassificationNode>(dbContext), IFaqCategoryRepository
    {
        public async Task<IReadOnlyCollection<ClassificationNode>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken)
            => (await ApplySpecification(new GetAllFaqCategoriesSpecification())
                .Cacheable().ToListAsync(cancellationToken)).AsReadOnly();
    }
}
