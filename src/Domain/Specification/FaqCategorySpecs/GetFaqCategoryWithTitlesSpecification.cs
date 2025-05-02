namespace GamaEdtech.Domain.Specification.FaqCategorySpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.FaqCategorySpecs.Criterias;

    public class GetFaqCategoryWithTitlesSpecification : BaseSpecification<ClassificationNode>
    {
        private readonly IEnumerable<string> titles;

        public GetFaqCategoryWithTitlesSpecification(IEnumerable<string> titles)
        {
            this.titles = titles;
            _ = Query.Where(Criteria().ToExpression());
        }
        protected override CriteriaSpecification<ClassificationNode> Criteria()
            => new CheckFaqCategoryTitleCriteria(titles);
    }
}
