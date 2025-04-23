namespace GamaEdtech.Domain.Specification.FaqSpecs.Criterias
{
    using GamaEdtech.Domain;
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;

#pragma warning disable S101
    public class CheckFaqDateTimeCriteria(DateTime? fromDateTime, DateTime? toDateTime) : CriteriaSpecification<Faq>
    {
        private readonly DateTime? fromDateTime = fromDateTime == default ? DateTime.MinValue : fromDateTime;
        private readonly DateTime? toDateTime = toDateTime == default ? DateTime.MaxValue : toDateTime;

        public override Expression<Func<Faq, bool>> ToExpression() => fromDateTime == default && toDateTime == default
                ? (current => true)
                : (current => current.CreateDate >= fromDateTime && current.CreateDate < toDateTime);
    }
#pragma warning restore S101
}
