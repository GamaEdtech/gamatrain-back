namespace GamaEdtech.Domain.DataAccess.Requests.FaqRequests
{
    using GamaEdtech.Common.Core.Extensions;

    public class GetFaqWithDynamicFilterRequest
    {
        public IEnumerable<string>? FaqCategoriesTitle { get; init; }
        public CustomOrderBy CustomOrderBy { get; init; }
        public CustomDateFormat CustomDateFormat { get; init; }
        = CustomDateFormat.ToSolarDate;
        public DateTime? FromDate { get; init; }
        public DateTime? ToDate { get; init; }

        public string? SummaryOfQuestion { get; set; }

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
