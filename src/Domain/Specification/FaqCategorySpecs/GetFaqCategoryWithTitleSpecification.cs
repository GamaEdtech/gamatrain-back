namespace GamaEdtech.Domain.Specification.FaqCategorySpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs.Criterias;

    public class GetFaqCategoryWithTitleSpecification : BaseSpecification<FaqCategory>
    {
        private readonly string title;

        public GetFaqCategoryWithTitleSpecification(string title)
        {
            this.title = title;
            _ = Query.Where(Criteria().ToExpression());
        }

        protected override CriteriaSpecification<FaqCategory> Criteria()
            => new CheckFaqCategoryTitleCriteria(title);
    }
}
