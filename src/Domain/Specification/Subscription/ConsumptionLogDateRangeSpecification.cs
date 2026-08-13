namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    /// <summary>Either bound can be null - an admin listing everything doesn't need to supply both.</summary>
    public sealed class ConsumptionLogDateRangeSpecification(DateTimeOffset? fromDate, DateTimeOffset? toDate) : SpecificationBase<SubscriptionQuotaConsumptionLog>
    {
        public override Expression<Func<SubscriptionQuotaConsumptionLog, bool>> Expression() =>
            (t) => (fromDate == null || t.CreationDate >= fromDate) && (toDate == null || t.CreationDate <= toDate);
    }
}
