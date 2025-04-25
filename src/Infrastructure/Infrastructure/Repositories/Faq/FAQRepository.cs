namespace GamaEdtech.Infrastructure.Repositories.Faq
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.Faq;
    using GamaEdtech.Infrastructure.EntityFramework.Context;
    using GamaEdtech.Infrastructure.Repositories;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class FaqRepository(ApplicationDBContext dbContext) : BaseRepository<Faq>(dbContext),
        IFaqRepository
    {
    }
}
