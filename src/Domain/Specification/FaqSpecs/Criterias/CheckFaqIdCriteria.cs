namespace GamaEdtech.Domain.Specification.FaqSpecs.Criterias
{
    using System;
    using System.Linq.Expressions;

    using GamaEdtech.Domain.Entity;

    public class CheckFaqIdCriteria(Guid? faqId) : CriteriaSpecification<Faq>
    {
        public override Expression<Func<Faq, bool>> ToExpression() =>
            faqId.HasValue ? (current => current.Id == faqId) : (current => true);
    }
}
