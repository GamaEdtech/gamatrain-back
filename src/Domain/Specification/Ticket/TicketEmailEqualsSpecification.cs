namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class TicketEmailEqualsSpecification(string email) : SpecificationBase<Ticket>
    {
        public override Expression<Func<Ticket, bool>> Expression()
        {
            var value = email.Trim();
            return (t) => t.Email == value;
        }
    }
}
