namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class ClassificationNodeTreeMapperExtension
    {
        public static IEnumerable<ClassificationNodeResponse> MapToResult([NotNull] this IReadOnlyList<ClassificationNodeTree> tree)
        {
            var results = new List<ClassificationNodeResponse>(tree.Count);
            foreach (var node in tree)
            {
                results.Add(MapNode(node));
            }
            return results;
        }

        private static ClassificationNodeResponse MapNode(ClassificationNodeTree node)
        {
            var result = new ClassificationNodeResponse
            {
                Id = node.ClassificationNode.Id,
                Title = node.ClassificationNode.Title,
                NodeType = node.ClassificationNode.NodeType.ToDisplay(),
                CreateDate = node.ClassificationNode.CreateDate,
                LastUpdatedDate = node.ClassificationNode.LastUpdatedDate
            };

            if (node.Children != null && node.Children.Count > 0)
            {
                var children = new List<ClassificationNodeResponse>(node.Children.Count);
                foreach (var child in node.Children)
                {
                    children.Add(MapNode(child));
                }
                result = result with { Children = children };
            }

            return result;
        }
    }
}
