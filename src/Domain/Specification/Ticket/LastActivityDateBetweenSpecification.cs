namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class LastActivityDateBetweenSpecification(DateTimeOffset? start, DateTimeOffset? end) : SpecificationBase<Ticket>
    {
        public override Expression<Func<Ticket, bool>> Expression() => (t) => (start == null || t.LastActivityDate >= start) && (end == null || t.LastActivityDate <= end);
    }
}
