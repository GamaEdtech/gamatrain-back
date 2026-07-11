namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class PlanIdEqualsSpecification(long subscriptionPlanId) : SpecificationBase<SubscriptionPlanPrice>
    {
        public override Expression<Func<SubscriptionPlanPrice, bool>> Expression() => (t) => t.SubscriptionPlanId == subscriptionPlanId;
    }
}
