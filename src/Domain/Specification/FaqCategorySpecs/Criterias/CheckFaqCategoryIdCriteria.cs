namespace GamaEdtech.Domain.Specification.FaqCategorySpecs.Criterias
{
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;
#pragma warning disable S101
    public class CheckFaqCategoryIdCriteria(Guid faqCategoryId) : CriteriaSpecification<FaqCategory>
    {
        public override Expression<Func<FaqCategory, bool>> ToExpression()
            => current => current.Id == faqCategoryId;
    }
#pragma warning restore S101
}
