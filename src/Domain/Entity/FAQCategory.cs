namespace GamaEdtech.Domain.Entity
{
    using System.Collections.ObjectModel;

    using GamaEdtech.Common.Resources;
    using GamaEdtech.Domain;
    using GamaEdtech.Domain.Valueobjects;

    public class FaqCategory : AggregateRoot
    {
        #region Ctors
        private FaqCategory()
        {
            Title = string.Empty;
            FaqCategoryType = FaqCategoryType.None;
            HierarchyPath = new HierarchyPath("");
            fAQAndFAQCategories = [];
        }
        private FaqCategory(string title, FaqCategoryType categoryType, HierarchyPath hierarchyPath)
        {
            Title = title;
            FaqCategoryType = categoryType;
            HierarchyPath = hierarchyPath;
            fAQAndFAQCategories = [];
        }
        #endregion

        #region Propeties
        public string Title { get; private set; }
        public FaqCategoryType FaqCategoryType { get; private set; }
        public HierarchyPath HierarchyPath { get; private set; }
        #endregion

        #region Relation
        #region ForeignKey
        #endregion

        #region ICollaction
        private readonly List<FaqAndFaqCategory> fAQAndFAQCategories;
        public IReadOnlyCollection<FaqAndFaqCategory> FAQAndFAQCategories => fAQAndFAQCategories;
        #endregion
        #endregion

        #region Functionalities
        public static FaqCategory Create(string title, FaqCategoryType categoryType, FaqCategory? parent = null)
        {
            var defaultSegment = categoryType switch
            {
                FaqCategoryType.Board => "1",
                FaqCategoryType.Grade => "2",
                FaqCategoryType.Subject => "3",
                FaqCategoryType.Topic => "4",
                _ => throw new ArgumentException("Invalid CategoryType", nameof(categoryType))
            };

            HierarchyPath newPath;

            if (parent == null)
            {
                if (categoryType != FaqCategoryType.Board)
                {
                    throw new InvalidOperationException(ExceptionsString.RootCategoryMustBeOfTypeBoard);
                }

                newPath = HierarchyPath.FromString($"/{defaultSegment}/");
            }
            else
            {
                switch (categoryType)
                {
                    case FaqCategoryType.Grade:
                        if (parent.FaqCategoryType != FaqCategoryType.Board)
                        {
                            throw new InvalidOperationException(ExceptionsString.GradeCategoryMustBeChildOfBoard);
                        }

                        break;
                    case FaqCategoryType.Subject:
                        if (parent.FaqCategoryType != FaqCategoryType.Grade)
                        {
                            throw new InvalidOperationException(ExceptionsString.SubjectCategoryMustBeChildOfGrade);
                        }

                        break;
                    case FaqCategoryType.Topic:
                        if (parent.FaqCategoryType is not FaqCategoryType.Subject and not FaqCategoryType.Topic)
                        {
                            throw new InvalidOperationException(ExceptionsString.TopicCategoryMustBeChildOfSubjectOrTopic);
                        }

                        break;
                    case FaqCategoryType.Board:
                        break;
                    default:
                        break;
                }

                newPath = parent.HierarchyPath.GetDescendant(defaultSegment, null);
            }

            return new FaqCategory(title, categoryType, newPath);
        }
        public static IReadOnlyList<FaqCategoryTree> BuildHierarchyTree(IEnumerable<FaqCategory> categories)
        {
            var nodes = categories
                .Select(c => new FaqCategoryTree(c))
                .GroupBy(n => n.Category.HierarchyPath.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var roots = new List<FaqCategoryTree>();

            foreach (var group in nodes.Values)
            {
                foreach (var node in group)
                {
                    var parentPath = GetParentPath(node.Category.HierarchyPath.Value);
                    if (!string.IsNullOrEmpty(parentPath) && nodes.TryGetValue(parentPath, out var parentGroup))
                    {
                        parentGroup[0].Children.Add(node);
                    }
                    else
                    {
                        roots.Add(node);
                    }
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
    public enum FaqCategoryType
    {
        None = -1,
        Board = 0,
        Grade = 1,
        Subject = 2,
        Topic = 3
    }
    #endregion

    public record FaqCategoryTree(FaqCategory Category)
    {
        public Collection<FaqCategoryTree> Children { get; init; } = [];
    }
}
