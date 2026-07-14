namespace GamaEdtech.Domain.Specification.Content
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class OwnerUserIdEqualsSpecification(long ownerUserId) : SpecificationBase<ContentOwnerCommission>
    {
        public override Expression<Func<ContentOwnerCommission, bool>> Expression() => (t) => t.OwnerUserId == ownerUserId;
    }
}
