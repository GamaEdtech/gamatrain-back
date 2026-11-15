namespace GamaEdtech.Presentation.ViewModel.UserActivity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary><c>GET admin/activities/posts</c>.</summary>
    public sealed class TopPostsRequestViewModel
    {
        /// <summary>The last this many days (1-365, default 30).</summary>
        [Display]
        public int? Days { get; set; }

        /// <summary>How many posts (1-100, default 20).</summary>
        [Display]
        public int? Size { get; set; }
    }

    public sealed class PostActivityResponseViewModel
    {
        public long PostId { get; set; }
        public string? Title { get; set; }
        public int Readers { get; set; }
        public int Listeners { get; set; }
        public int FullListens { get; set; }
    }

    public sealed class UserActivityItemResponseViewModel
    {
        public DateTimeOffset Day { get; set; }
        public string? Type { get; set; }
        public long PostId { get; set; }
        public string? Title { get; set; }
    }
}
