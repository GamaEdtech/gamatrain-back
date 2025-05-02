namespace GamaEdtech.Domain.Specification.FaqCategorySpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs.Criterias;

    public class GetFaqCategoryWithTitleSpecification : BaseSpecification<ClassificationNode>
    {
        private readonly string[] titles;

        public GetFaqCategoryWithTitleSpecification(string[] titles)
        {
            this.titles = titles;
            _ = Query.Where(Criteria().ToExpression());
        }

        protected override CriteriaSpecification<ClassificationNode> Criteria()
            => new CheckFaqCategoryTitleCriteria(titles);
    }
}
