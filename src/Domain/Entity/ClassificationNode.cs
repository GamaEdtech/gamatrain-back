namespace GamaEdtech.Domain.Entity
{
    using System.Collections.ObjectModel;

    using GamaEdtech.Common.Resources;
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

            HierarchyPath newPath;


            if (parents == null || !parents.Any())
            {
                if (nodeType != ClassificationNodeType.Board)
                {
                    throw new InvalidOperationException(ExceptionsString.RootCategoryMustBeOfTypeBoard);
                }

                newPath = HierarchyPath.FromString($"/{defaultSegment}/");
            }
            else
            {
                var paths = parents
                    .Select(parent => parent.HierarchyPath.GetDescendant(defaultSegment, null).Value);
                newPath = HierarchyPath.FromString(string.Join(HierarchyPath.ParentPathSeparator, paths));
            }

            return new ClassificationNode(title, nodeType, newPath);
        }
        public static IReadOnlyList<ClassificationNodeTree> BuildHierarchyTree(IEnumerable<ClassificationNode> classificationNodes)
        {
            var nodes = classificationNodes
                .Select(c => new ClassificationNodeTree(c))
                .ToDictionary(n => n.ClassificationNode.HierarchyPath.Value);

            var roots = new List<ClassificationNodeTree>();

            foreach (var node in nodes.Values)
            {
                var paths = node.ClassificationNode.HierarchyPath.GetPaths();
                var isRoot = true;

                foreach (var path in paths)
                {
                    var parentPath = GetParentPath(path);
                    if (!string.IsNullOrEmpty(parentPath) && nodes.TryGetValue(parentPath, out var parentNode))
                    {
                        parentNode.Children.Add(node);
                        isRoot = false;
                    }
                }

                if (isRoot)
                {
                    roots.Add(node);
                }
            }

            return roots;
        }
        private static string? GetParentPath(string path)
        {
            var trimmed = path.TrimEnd('/');
            var lastIndex = trimmed.LastIndexOf('/');
            return lastIndex <= 0 ? null : trimmed[..(lastIndex + 1)];
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
