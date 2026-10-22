namespace GamaEdtech.Data.Dto.Dashboard
{
    /// <summary>The admin dashboard's "needs attention" counts, cached for a minute (GeneratedDate says when).</summary>
    public sealed class AdminDashboardSummaryDto
    {
        public IList<AttentionItemDto> Items { get; set; } = [];

        public DateTimeOffset GeneratedDate { get; set; }
    }
}
