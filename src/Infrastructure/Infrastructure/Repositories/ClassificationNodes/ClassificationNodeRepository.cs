namespace GamaEdtech.Infrastructure.Repositories.ClassificationNodes
{
    using EFCoreSecondLevelCacheInterceptor;

    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.ClassificationNodes;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs;
    using GamaEdtech.Infrastructure.EntityFramework.Context;

    using Microsoft.EntityFrameworkCore;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ClassificationNodeRepository(ApplicationDBContext dbContext) :
        BaseRepository<ClassificationNode>(dbContext), IClassificationNodeRepository
    {
        public async Task<IReadOnlyCollection<ClassificationNode>> ListAsyncWithSecondaryLevelCacheAsync(CancellationToken cancellationToken)
            => (await ApplySpecification(new GetAllClassificationNodeSpecification())
                .Cacheable().ToListAsync(cancellationToken)).AsReadOnly();
    }
}
