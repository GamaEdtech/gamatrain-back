namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSubscriptionStatusEqualsSpecification(UserSubscriptionStatus status) : SpecificationBase<UserSubscription>
    {
        public override Expression<Func<UserSubscription, bool>> Expression() => (t) => t.Status.Equals(status);
    }
}
