namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias;

    public class GetClassificationNodeWithTitlesSpecification : BaseSpecification<ClassificationNode>
    {
        private readonly IEnumerable<string> titles;

        public GetClassificationNodeWithTitlesSpecification(IEnumerable<string> titles)
        {
            this.titles = titles;
            _ = Query.Where(Criteria().ToExpression());
        }
        protected override CriteriaSpecification<ClassificationNode> Criteria()
            => new CheckClassificationNodeTitleCriteria(titles);
    }
}
