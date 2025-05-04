namespace GamaEdtech.Data.Dto.FaqManager
{
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;

    public class GetFaqWithDynamicFilterDto
    {
        public IEnumerable<string>? FaqCategoriesTitle { get; init; }
        public CustomOrderBy CustomOrderBy { get; init; }
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }

        public string? SummaryOfQuestion { get; set; }
    }

}
