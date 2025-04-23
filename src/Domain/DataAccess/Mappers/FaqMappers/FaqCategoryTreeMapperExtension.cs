namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class FaqCategoryTreeMapperExtension
    {
        public static IEnumerable<FaqCategoryResponse> MapToResult([NotNull] this IReadOnlyList<FaqCategoryTree> tree)
        {
            var results = new List<FaqCategoryResponse>(tree.Count);
            foreach (var node in tree)
            {
                results.Add(MapNode(node));
            }
            return results;
        }

        private static FaqCategoryResponse MapNode(FaqCategoryTree node)
        {
            //create from root
            var result = new FaqCategoryResponse
            {
                Id = node.Category.Id,
                Title = node.Category.Title,
            };

            // recursive map from node with have child 
            if (node.Children != null && node.Children.Count > 0)
            {
                var children = new List<FaqCategoryResponse>(node.Children.Count);
                foreach (var child in node.Children)
                {
                    children.Add(MapNode(child));
                }
                // create immutable child  record
                result = result with { Children = children };
            }

            return result;
        }
    }
}
