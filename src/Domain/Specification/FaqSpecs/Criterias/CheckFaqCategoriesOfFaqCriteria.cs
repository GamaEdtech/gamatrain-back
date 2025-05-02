namespace GamaEdtech.Domain.Specification.FaqSpecs.Criterias
{
    using System.Linq.Expressions;

    using GamaEdtech.Domain.Entity;

    public class CheckFaqCategoriesOfFaqCriteria(IEnumerable<string>? titles) : CriteriaSpecification<Faq>
    {
        public override Expression<Func<Faq, bool>> ToExpression() => titles == null || titles.Any()
                ? (current => true)
                : (current => current.ClassificationNodeRelationships
                .Any(a => titles.Any(t => a.ClassificationNode != null && t == a.ClassificationNode.Title)));
    }
}
