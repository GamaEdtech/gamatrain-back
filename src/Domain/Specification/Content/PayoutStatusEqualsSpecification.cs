namespace GamaEdtech.Domain.Specification.Content
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PayoutStatusEqualsSpecification(PayoutStatus status) : SpecificationBase<CommissionPayout>
    {
        public override Expression<Func<CommissionPayout, bool>> Expression() => (t) => t.Status == status;
    }
}
