namespace GamaEdtech.Domain.Specification.FaqCategorySpecs
{
    using System.Collections.ObjectModel;

    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs.Criterias;

    public class GetFaqCategoryWithTitlesSpecification : BaseSpecification<FaqCategory>
    {
        private readonly Collection<string> titles;

        public GetFaqCategoryWithTitlesSpecification(Collection<string> titles)
        {
            this.titles = titles;
            _ = Query.Where(Criteria().ToExpression());
        }
        protected override CriteriaSpecification<FaqCategory> Criteria()
            => new CheckFaqCategoryTitleCriteria(titles);
    }
}
