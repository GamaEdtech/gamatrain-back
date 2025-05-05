namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias
{
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;
#pragma warning disable S101
    public class CheckClassificationNodeIdCriteria : CriteriaSpecification<ClassificationNode>
    {
        private readonly IEnumerable<Guid> classficationNodeIds;
        private readonly Guid? classficationNodeId;

        public CheckClassificationNodeIdCriteria(Guid? classficationNodeId) => this.classficationNodeId = classficationNodeId;
        public CheckClassificationNodeIdCriteria(IEnumerable<Guid> classficationNodeIds) => this.classficationNodeIds = classficationNodeIds;
        public override Expression<Func<ClassificationNode, bool>> ToExpression()
            => classficationNodeId.HasValue && classficationNodeIds == null
                ? (current => current.Id == classficationNodeId.Value)
                : classficationNodeIds is not null && classficationNodeIds.Any()
                ? (current => classficationNodeIds.Any(a => a == current.Id))
                : (current => true);
    }
#pragma warning restore S101
}
