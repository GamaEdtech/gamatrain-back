namespace GamaEdtech.Domain.Repositories.Faq
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IFaqRepository : IBaseRepository<Faq>
    {
    }
}
