namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    // Same meaning as the admin list's bold row / HasNewReply: the ticket itself, or any reply on it, is unread by admin.
    public sealed class TicketUnreadByAdminSpecification : SpecificationBase<Ticket>
    {
        public override Expression<Func<Ticket, bool>> Expression() => (t) => !t.IsReadByAdmin || t.TicketReplys.Any(r => !r.IsReadByAdmin);
    }
}
