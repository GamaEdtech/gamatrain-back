namespace GamaEdtech.Domain.DataAccess.Responses.FaqResponses
{
    public record ClassificationNodeResponse
    {
        public Guid Id { get; init; }
        public required string Title { get; init; }
        public DateTime CreateDate { get; init; }
        public string NodeType { get; init; }
        public DateTime LastUpdatedDate { get; init; }
        public IEnumerable<ClassificationNodeResponse> Children { get; init; } = [];
    }
}
