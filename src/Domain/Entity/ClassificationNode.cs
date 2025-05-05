namespace GamaEdtech.Domain.Entity
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    using GamaEdtech.Domain;
    using GamaEdtech.Domain.Valueobjects;

    public class ClassificationNode : AggregateRoot
    {
        #region Ctors
        private ClassificationNode()
        {
            Title = string.Empty;
            NodeType = ClassificationNodeType.None;
            HierarchyPath = new HierarchyPath("");
            classificationNodeRelationships = [];
            AllowMultipleSelection = true;
        }
        private ClassificationNode(string title, ClassificationNodeType categoryType, HierarchyPath hierarchyPath)
        {
            Title = title;
            NodeType = categoryType;
            HierarchyPath = hierarchyPath;
            classificationNodeRelationships = [];
            AllowMultipleSelection = true;
        }
        #endregion

        #region Propeties
        public string Title { get; private set; }
        public string? Image { get; private set; }
        public bool AllowMultipleSelection { get; private set; }
        public ClassificationNodeType NodeType { get; private set; }
        public HierarchyPath HierarchyPath { get; private set; }
        #endregion

        #region Relation
        #region ForeignKey
        #endregion

        #region ICollaction
        private readonly List<ClassificationNodeRelationship> classificationNodeRelationships;
        public IReadOnlyCollection<ClassificationNodeRelationship> ClassificationNodeRelationships => classificationNodeRelationships;
        #endregion
        #endregion

        #region Functionalities
        public static ClassificationNode Create(string title, ClassificationNodeType nodeType, IEnumerable<ClassificationNode>? parents = null)
        {
            var defaultSegment = nodeType switch
            {
                ClassificationNodeType.Board => "1",
                ClassificationNodeType.Grade => "2",
                ClassificationNodeType.Subject => "3",
                ClassificationNodeType.Topic => "4",
                _ => throw new ArgumentException("Invalid ClassificationNodeType", nameof(nodeType))
            };

            var newPath = parents != null && parents.Any()
            ? string.Join(HierarchyPath.ParentPathSeparator, parents.Select(s => s.Id).ToArray())
            : "";

            return new ClassificationNode(title, nodeType, new HierarchyPath(newPath));
        }
        public static IReadOnlyList<ClassificationNodeTree> BuildHierarchyTree(IEnumerable<ClassificationNode> classificationNodes)
        {
            var nodes = classificationNodes
                .Select(c => new ClassificationNodeTree(c))
                .ToDictionary(g => g.ClassificationNode.Id, g => g);

            var roots = new List<ClassificationNodeTree>();

            roots.AddRange(nodes
                .Where(c => string.IsNullOrEmpty(c.Value.ClassificationNode.HierarchyPath?.Value))
                .Select(s => s.Value)
                .ToList());

            foreach (var node in nodes)
            {
                var parentIds = node.Value.ClassificationNode.HierarchyPath?.Value
                    ?.Split(HierarchyPath.ParentPathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => Guid.Parse(c.Trim()));

                if (parentIds == null || !parentIds.Any())
                {
                    continue;
                }

                foreach (var parentId in parentIds)
                {
                    if (nodes.TryGetValue(parentId, out var parentClassificationNode))
                    {
                        parentClassificationNode.Children.Add(node.Value);
                    }
                }
            }

            return roots;
        }
        public void AppendRelationship(ClassificationNodeRelationship nodeRelationship)
        {
            classificationNodeRelationships.Add(nodeRelationship);
            return;
        }
        #endregion

        #region Domain Events

        #endregion
    }

    #region Enums
    public enum ClassificationNodeType
    {
        None = -1,
        Board = 0,
        Grade = 1,
        Subject = 2,
        Topic = 3
    }
    #endregion

    public record ClassificationNodeTree(ClassificationNode ClassificationNode)
    {
        public Collection<ClassificationNodeTree> Children { get; init; } = [];
    }
}
