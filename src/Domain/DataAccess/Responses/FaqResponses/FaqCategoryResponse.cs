namespace GamaEdtech.Domain.DataAccess.Responses.FaqResponses
{
    using GamaEdtech.Common.Core.Extensions;

    public record FaqCategoryResponse
    {
        public Guid Id { get; init; }
        public required string Title { get; init; }
        public CustomDateTimeFormat CreateDate { get; init; }
        public CustomDateTimeFormat LastUpdatedDate { get; init; }
        public IEnumerable<FaqCategoryResponse> Children { get; init; } = [];
    }
}
