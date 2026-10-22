namespace GamaEdtech.Presentation.ViewModel.Dashboard
{
    public sealed class AdminDashboardSummaryResponseViewModel
    {
        public IEnumerable<AttentionItemResponseViewModel> Items { get; set; } = [];

        /// <summary>When the counts were computed (they are cached for a minute).</summary>
        public DateTimeOffset GeneratedDate { get; set; }
    }

    public sealed class AttentionItemResponseViewModel
    {
        /// <summary>ContactUs, SchoolImages, SchoolContributions, SchoolIssues, SchoolImageRemovals, SchoolComments, Posts, PostComments or Payouts.</summary>
        public string? Key { get; set; }

        public int Count { get; set; }

        public int NewLast7Days { get; set; }

        public DateTimeOffset? OldestDate { get; set; }
    }
}
