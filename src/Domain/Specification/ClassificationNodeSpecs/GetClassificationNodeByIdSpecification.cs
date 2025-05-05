namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias;

    public class GetClassificationNodeByIdSpecification : BaseSpecification<ClassificationNode>
    {
        private readonly IEnumerable<Guid> classificationNodeIds;

        public GetClassificationNodeByIdSpecification(IEnumerable<Guid> classificationNodeIds)
        {
            this.classificationNodeIds = classificationNodeIds;
            _ = Query.Where(Criteria().ToExpression());
        }
        protected override CriteriaSpecification<ClassificationNode> Criteria()
            => new CheckClassificationNodeIdCriteria(classificationNodeIds);
    }
}
