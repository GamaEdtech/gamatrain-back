namespace GamaEdtech.Domain.DataAccess.Responses.FaqResponses
{
    public class FaqResponse : BaseResponse
    {
        public Guid Id { get; init; }
        public string SummaryOfQuestion { get; init; }
        public string Question { get; init; }
        public IEnumerable<FaqCategoryResponse> FaqCategoryTree { get; init; }
    }
}
