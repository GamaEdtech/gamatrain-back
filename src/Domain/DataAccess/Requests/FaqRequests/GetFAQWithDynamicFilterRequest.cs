namespace GamaEdtech.Domain.DataAccess.Requests.FaqRequests
{
    public class GetFaqWithDynamicFilterRequest
    {
        public IEnumerable<string>? FaqCategoriesTitle { get; init; }
        public CustomOrderBy CustomOrderBy { get; init; }
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }
        public Guid? FaqId { get; init; }

        public string? SummaryOfQuestion { get; init; }

        public bool HasValue() => (FaqCategoriesTitle is not null && FaqCategoriesTitle.Any()) ||
                 FromDate != null || ToDate != null || (SummaryOfQuestion != null &&
                string.IsNullOrEmpty(SummaryOfQuestion));
    }
    public enum CustomOrderBy
    {
        OrderByCreateDateAscending,
        OrderByCreateDateDescending,
    }
}
