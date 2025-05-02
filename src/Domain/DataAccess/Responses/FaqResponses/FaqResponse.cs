namespace GamaEdtech.Domain.DataAccess.Responses.FaqResponses
{
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    public class FaqResponse : BaseResponse
    {
        public Guid Id { get; init; }
        public string SummaryOfQuestion { get; init; }
        public string Question { get; init; }
        public IEnumerable<ClassificationNodeResponse> FaqCategoryTree { get; init; }
        public IEnumerable<FileResponse>? FileResponses { get; init; }
    }
}
