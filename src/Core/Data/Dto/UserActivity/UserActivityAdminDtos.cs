namespace GamaEdtech.Data.Dto.UserActivity
{
    /// <summary>One post's reads and listens over a window, for the admin activity page.</summary>
    public sealed class PostActivityDto
    {
        public long PostId { get; set; }
        public string? Title { get; set; }

        /// <summary>Different signed-in users who read it.</summary>
        public int Readers { get; set; }

        /// <summary>Different users who started its podcast.</summary>
        public int Listeners { get; set; }

        /// <summary>Different users who heard at least 80% of its podcast.</summary>
        public int FullListens { get; set; }
    }

    /// <summary>One read or listen of one user.</summary>
    public sealed class UserActivityItemDto
    {
        public DateTimeOffset Day { get; set; }
        public required string Type { get; set; }
        public long PostId { get; set; }
        public string? Title { get; set; }
    }
}
