namespace GamaEdtech.Data.Dto.ProfileProgress
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The signed-in user's progress: profile level and its quests, points and leaderboard rank, practice streak, and
    /// their school's shared goal. See docs/business/identity-and-access.md, "Profile progress".
    /// </summary>
    public sealed class ProfileProgressDto
    {
        public required UserRateLevel Level { get; set; }

        /// <summary>Profile percent, 0-100 (<see cref="UserRateLevel.Score"/>).</summary>
        public int Percent { get; set; }

        /// <summary>The next level, null at Complete.</summary>
        public UserRateLevel? NextLevel { get; set; }

        /// <summary>The percent where <see cref="NextLevel"/> starts.</summary>
        public int? NextLevelAtPercent { get; set; }

        public required IReadOnlyList<ProfileQuestDto> Quests { get; set; }

        /// <summary>Points balance (<c>CurrentBalance</c>), the number the leaderboard ranks by.</summary>
        public long Points { get; set; }

        /// <summary>Place on the leaderboard: 1 + users (admins excluded) with more points. Null for an admin.</summary>
        public int? Rank { get; set; }

        public required PracticeStreakDto Practice { get; set; }

        /// <summary>The user's school, null when they haven't picked one.</summary>
        public ProfileSchoolDto? School { get; set; }
    }

    /// <summary>One profile field to fill. <see cref="Code"/> matches the dashboard's <c>profileCompletion.notComplete</c>.</summary>
    public sealed class ProfileQuestDto
    {
        public required string Code { get; set; }

        public bool Done { get; set; }

        /// <summary>Profile percent the field is worth.</summary>
        public int Percent { get; set; }
    }

    /// <summary>Days with at least one practice question answered (<c>TestSubmission</c>), counted in UTC.</summary>
    public sealed class PracticeStreakDto
    {
        /// <summary>Days in a row up to today, or up to yesterday while today has no answer yet.</summary>
        public int Current { get; set; }

        public int Best { get; set; }

        public bool PractisedToday { get; set; }

        /// <summary>The last 7 days, oldest first, today last.</summary>
        public required IReadOnlyList<bool> LastSevenDays { get; set; }
    }

    /// <summary>The user's school and its shared review goal. Only counts: no other member is named.</summary>
    public sealed class ProfileSchoolDto
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        /// <summary>Approved reviews.</summary>
        public int Reviews { get; set; }

        /// <summary>The next milestone above <see cref="Reviews"/> (<c>ProfileProgressRules.NextGoal</c>).</summary>
        public int ReviewGoal { get; set; }

        public int Photos { get; set; }

        public int Students { get; set; }

        public int Teachers { get; set; }

        /// <summary>Different people whose contribution to this school was approved this calendar month (UTC).</summary>
        public int HelpersThisMonth { get; set; }

        /// <summary>The user's own review of this school: null for none (or rejected), Review while waiting, Confirmed once approved.</summary>
        public Status? MyReview { get; set; }

        /// <summary>Points an approved contribution earns, from the admin settings.</summary>
        public long ReviewPoints { get; set; }

        public long PhotoPoints { get; set; }

        public long EditPoints { get; set; }
    }
}
