namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class PlanPriceIdEqualsSpecification(long subscriptionPlanPriceId) : SpecificationBase<SubscriptionPlanGatewayMapping>
    {
        public override Expression<Func<SubscriptionPlanGatewayMapping, bool>> Expression() => (t) => t.SubscriptionPlanPriceId == subscriptionPlanPriceId;
    }
}
