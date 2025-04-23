namespace GamaEdtech.Domain
{
    using Ardalis.Specification;

    public abstract class BaseSpecification<T> : Specification<T>
        where T : IEntity
    {
        protected abstract CriteriaSpecification<T> Criteria();
    }
}
