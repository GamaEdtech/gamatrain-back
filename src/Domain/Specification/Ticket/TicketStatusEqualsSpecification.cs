namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TicketStatusEqualsSpecification(TicketStatus status) : SpecificationBase<Ticket>
    {
        public override Expression<Func<Ticket, bool>> Expression() => (t) => t.Status == status;
    }
}
