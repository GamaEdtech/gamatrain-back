namespace GamaEdtech.Domain.DataAccess.Requests.FaqRequests
{
    using System.Collections.ObjectModel;

    public class GetFaqWithDynamicFilterRequest
    {
        public Collection<string>? FaqCategoriesTitle { get; init; }
        public CustomOrderBy CustomOrderBy { get; init; }
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }

        public string? SummaryOfQuestion { get; set; }

        public bool HasValue() => (FaqCategoriesTitle is not null && FaqCategoriesTitle.Count != 0) ||
                 FromDate != null || ToDate != null || (SummaryOfQuestion != null &&
                string.IsNullOrEmpty(SummaryOfQuestion));
    }
    public enum CustomOrderBy
    {
        OrderByCreateDateAscending,
        OrderByCreateDateDescending,
    }
}
