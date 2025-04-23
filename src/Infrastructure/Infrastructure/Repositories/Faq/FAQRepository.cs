namespace GamaEdtech.Infrastructure.Repositories.Faq
{
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Infrastructure.EntityFramework.Context;
    using GamaEdtech.Infrastructure.Repositories;
    public class FaqRepository(ApplicationDBContext dbContext) : BaseRepository<Faq>(dbContext)
    {
    }
}
