namespace GamaEdtech.Data.Dto.Dashboard
{
    /// <summary>One "needs attention" queue on the admin dashboard.</summary>
    public sealed class AttentionItemDto
    {
        /// <summary>Stable key the frontend maps to a label and admin page, e.g. ContactUs, SchoolImages.</summary>
        public required string Key { get; set; }

        public int Count { get; set; }

        /// <summary>Of Count, how many arrived in the last 7 days.</summary>
        public int NewLast7Days { get; set; }

        /// <summary>When the oldest waiting item arrived.</summary>
        public DateTimeOffset? OldestDate { get; set; }
    }
}
