namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Linq.Expressions;
    using System.Security.Claims;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class UserTicketsSpecification(ClaimsPrincipal user) : SpecificationBase<Ticket>
    {
        public override Expression<Func<Ticket, bool>> Expression()
        {
            var userId = user.UserId();
            var email = user.ConfirmedEmail();
            return email is null
                ? (t) => userId.Equals(t.UserId)
                : (t) => userId.Equals(t.UserId) || t.Email == email;
        }
    }
}
