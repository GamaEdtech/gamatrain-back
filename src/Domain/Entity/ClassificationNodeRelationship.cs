namespace GamaEdtech.Domain.Entity
{
    using GamaEdtech.Domain;

    public class ClassificationNodeRelationship : BaseEntity
    {
        #region Ctors
        private ClassificationNodeRelationship()
        {
        }
        private ClassificationNodeRelationship(Guid nodeRelationshipEntityId, Guid classificationNodeId, NodeRelationEntityType nodeRelationEntityType)
        {
            NodeRelationshipEntityId = nodeRelationshipEntityId;
            ClassificationNodeId = classificationNodeId;
            NodeRelationEntityType = nodeRelationEntityType;
        }
        #endregion

        #region Propeties
        public Guid NodeRelationshipEntityId { get; private set; }
        public Guid ClassificationNodeId { get; private set; }
        public NodeRelationEntityType NodeRelationEntityType { get; private set; }
        #endregion

        #region Relations
        #region ForeignKey
#pragma warning disable S1144 // Disable unused private setter warning for Faq and FaqCategory
        public virtual ClassificationNode ClassificationNode { get; private set; }
#pragma warning restore S1144

        #endregion

        #region ICollectiona

        #endregion
        #endregion

        #region Functionalities
        public static ClassificationNodeRelationship Create(Guid nodeRelationshipEntityId,
            Guid classificationNodeId, NodeRelationEntityType nodeRelationEntityType) =>
            new(nodeRelationshipEntityId, classificationNodeId, nodeRelationEntityType);
        #endregion
    }

    public enum NodeRelationEntityType
    {
        Faq
    }
}
