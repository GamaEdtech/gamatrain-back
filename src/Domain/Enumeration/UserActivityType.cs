namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>What a signed-in user did with a piece of content, as recorded by <c>UserActivity</c>. See
    /// docs/business/exams-and-content.md, "Reads and listens".</summary>
    public sealed class UserActivityType : Enumeration<UserActivityType, byte>
    {
        /// <summary>Read a post: stayed about 30 seconds or scrolled half of it.</summary>
        [Display]
        public static readonly UserActivityType PostRead = new(nameof(PostRead), 0);

        /// <summary>Started a post's podcast.</summary>
        [Display]
        public static readonly UserActivityType PodcastPlay = new(nameof(PodcastPlay), 1);

        /// <summary>Listened to at least 80% of a post's podcast.</summary>
        [Display]
        public static readonly UserActivityType PodcastListen = new(nameof(PodcastListen), 2);

        public UserActivityType()
        {
        }

        public UserActivityType(string name, byte value) : base(name, value)
        {
        }
    }
}
