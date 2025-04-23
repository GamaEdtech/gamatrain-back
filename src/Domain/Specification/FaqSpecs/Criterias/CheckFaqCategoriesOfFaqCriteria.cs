namespace GamaEdtech.Domain.Specification.FaqSpecs.Criterias
{
    using System.Collections.ObjectModel;
    using System.Linq.Expressions;

    using GamaEdtech.Domain.Entity;

    public class CheckFaqCategoriesOfFaqCriteria(Collection<string>? titles) : CriteriaSpecification<Faq>
    {
        public override Expression<Func<Faq, bool>> ToExpression() => titles == null || titles.Count == 0
                ? (current => true)
                : (current => current.FaqAndFaqCategories
                .Any(a => titles.Any(t => a.FaqCategory != null && t == a.FaqCategory.Title)));
    }
}
