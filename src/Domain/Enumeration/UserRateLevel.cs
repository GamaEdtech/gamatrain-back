namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class UserRateLevel : Enumeration<UserRateLevel, byte>
    {
        [Display]
        public static readonly UserRateLevel Beginner = new(nameof(Beginner), 0);

        [Display]
        public static readonly UserRateLevel Basic = new(nameof(Basic), 1);

        [Display]
        public static readonly UserRateLevel Intermediate = new(nameof(Intermediate), 2);

        [Display]
        public static readonly UserRateLevel Advanced = new(nameof(Advanced), 3);

        [Display]
        public static readonly UserRateLevel Complete = new(nameof(Complete), 4);

        public UserRateLevel()
        {
        }

        public UserRateLevel(string name, byte value) : base(name, value)
        {
        }

        /// <summary>Profile percent each filled field is worth; they add up to 100. Shared by <see cref="Score"/>, the SQL
        /// version in the profile list, and the profile progress quests.</summary>
        public const int AvatarWeight = 15;
        public const int FirstNameWeight = 5;
        public const int LastNameWeight = 5;
        public const int StatusWeight = 10;
        public const int BiographyWeight = 15;
        public const int SkillsWeight = 20;
        public const int ExperienceWeight = 30;

        /// <summary>A status sentence counts when it is longer than this.</summary>
        public const int MinStatusLength = 9;

        /// <summary>A biography counts when it is longer than this.</summary>
        public const int MinBiographyLength = 49;

        /// <summary>The percent where each level starts, in order: Basic, Intermediate, Advanced, Complete.</summary>
        public static readonly IReadOnlyList<(UserRateLevel Level, int AtPercent)> Thresholds = [(Basic, 20), (Intermediate, 40), (Advanced, 60), (Complete, 80)];

        /// <summary>
        /// The profile percent, 0-100: each filled field adds its weight. Until 2026-11-13 the photo and both names added
        /// their weight when they were missing, and a score of 60-79 was Intermediate, so nobody reached Advanced.
        /// </summary>
        public static int Score(string? avatar, string? firstName, string? lastName, string? currentStatusSentence, string? biography, IEnumerable<string?>? skills, IEnumerable<long>? experiences) =>
            (string.IsNullOrEmpty(avatar) ? 0 : AvatarWeight)
            + (string.IsNullOrEmpty(firstName) ? 0 : FirstNameWeight)
            + (string.IsNullOrEmpty(lastName) ? 0 : LastNameWeight)
            + (currentStatusSentence?.Length > MinStatusLength ? StatusWeight : 0)
            + (biography?.Length > MinBiographyLength ? BiographyWeight : 0)
            + (skills?.Any(t => !string.IsNullOrWhiteSpace(t)) == true ? SkillsWeight : 0)
            + (experiences?.Any() == true ? ExperienceWeight : 0);

        public static UserRateLevel Calculate(string? avatar, string? firstName, string? lastName, string? currentStatusSentence, string? biography, IEnumerable<string?>? skills, IEnumerable<long>? experiences) =>
            Calculate(Score(avatar, firstName, lastName, currentStatusSentence, biography, skills, experiences));

        public static UserRateLevel Calculate(int rate)
        {
            var level = Beginner;
            foreach (var (threshold, atPercent) in Thresholds)
            {
                if (rate >= atPercent)
                {
                    level = threshold;
                }
            }

            return level;
        }
    }
}
