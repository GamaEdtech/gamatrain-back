namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class ClassificationNodeTreeMapperExtension
    {
        public static IEnumerable<ClassificationNodeResponse> MapToResult([NotNull] this IReadOnlyList<ClassificationNodeTree> tree,
            CustomDateFormat customDateFormat)
        {
            var results = new List<ClassificationNodeResponse>(tree.Count);
            foreach (var node in tree)
            {
                results.Add(MapNode(node, customDateFormat));
            }
            return results;
        }

        private static ClassificationNodeResponse MapNode(ClassificationNodeTree node, CustomDateFormat customDateFormat)
        {
            var result = new ClassificationNodeResponse
            {
                Id = node.ClassificationNode.Id,
                Title = node.ClassificationNode.Title,
                CreateDate = node.ClassificationNode.CreateDate.ConvertToCustomDate(customDateFormat),
                LastUpdatedDate = node.ClassificationNode.LastUpdatedDate.ConvertToCustomDate(customDateFormat)
            };

            if (node.Children != null && node.Children.Count > 0)
            {
                var children = new List<ClassificationNodeResponse>(node.Children.Count);
                foreach (var child in node.Children)
                {
                    children.Add(MapNode(child, customDateFormat));
                }
                result = result with { Children = children };
            }

            return result;
        }
    }
}
