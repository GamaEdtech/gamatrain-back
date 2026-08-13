namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class ConsumptionLogFeatureCodeEqualsSpecification(string code) : SpecificationBase<SubscriptionQuotaConsumptionLog>
    {
        public override Expression<Func<SubscriptionQuotaConsumptionLog, bool>> Expression() => (t) => t.Feature!.Code == code;
    }
}
