namespace GamaEdtech.Domain.Specification.Content
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class CommissionReasonEqualsSpecification(CommissionReason reason) : SpecificationBase<ContentOwnerCommission>
    {
        public override Expression<Func<ContentOwnerCommission, bool>> Expression() => (t) => t.Reason == reason;
    }
}
