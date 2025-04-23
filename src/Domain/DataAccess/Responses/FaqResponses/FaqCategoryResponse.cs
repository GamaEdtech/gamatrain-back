namespace GamaEdtech.Domain.DataAccess.Responses.FaqResponses
{
    public record FaqCategoryResponse
    {
        public Guid Id { get; init; }
        public required string Title { get; init; }
        public IEnumerable<FaqCategoryResponse> Children { get; init; } = [];
    }
}
