namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class FaqCategoryTreeMapperExtension
    {
        public static IEnumerable<FaqCategoryResponse> MapToResult([NotNull] this IReadOnlyList<FaqCategoryTree> tree,
            CustomDateFormat customDateFormat)
        {
            var results = new List<FaqCategoryResponse>(tree.Count);
            foreach (var node in tree)
            {
                results.Add(MapNode(node, customDateFormat));
            }
            return results;
        }

        private static FaqCategoryResponse MapNode(FaqCategoryTree node, CustomDateFormat customDateFormat)
        {
            //create from root
            var result = new FaqCategoryResponse
            {
                Id = node.Category.Id,
                Title = node.Category.Title,
                CreateDate = node.Category.CreateDate.ConvertToCustomDate(customDateFormat),
                LastUpdatedDate = node.Category.LastUpdatedDate.ConvertToCustomDate(customDateFormat)
            };

            // recursive map from node with have child 
            if (node.Children != null && node.Children.Count > 0)
            {
                var children = new List<FaqCategoryResponse>(node.Children.Count);
                foreach (var child in node.Children)
                {
                    children.Add(MapNode(child, customDateFormat));
                }
                // create immutable child  record
                result = result with { Children = children };
            }

            return result;
        }
    }
}
