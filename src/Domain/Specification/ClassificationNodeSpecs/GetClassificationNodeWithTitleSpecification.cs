namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias;

    public class GetClassificationNodeWithTitleSpecification : BaseSpecification<ClassificationNode>
    {
        private readonly string[] titles;

        public GetClassificationNodeWithTitleSpecification(string[] titles)
        {
            this.titles = titles;
            _ = Query.Where(Criteria().ToExpression());
        }

        protected override CriteriaSpecification<ClassificationNode> Criteria()
            => new CheckClassificationNodeTitleCriteria(titles);
    }
}
