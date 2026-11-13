namespace GamaEdtech.Presentation.ViewModel.ProfileProgress
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    /// <summary><c>GET identities/profiles/progress</c>. See docs/business/identity-and-access.md, "Profile progress".</summary>
    public sealed class ProfileProgressResponseViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<UserRateLevel, byte>))]
        public UserRateLevel? Level { get; set; }

        /// <summary>Profile percent, 0-100.</summary>
        public int Percent { get; set; }

        /// <summary>The next level, null at Complete.</summary>
        [JsonConverter(typeof(EnumerationConverter<UserRateLevel, byte>))]
        public UserRateLevel? NextLevel { get; set; }

        public int? NextLevelAtPercent { get; set; }

        public IEnumerable<ProfileQuestViewModel>? Quests { get; set; }

        public long Points { get; set; }

        /// <summary>Leaderboard place; null for an admin.</summary>
        public int? Rank { get; set; }

        public PracticeStreakViewModel? Practice { get; set; }

        /// <summary>Null when the user hasn't picked a school.</summary>
        public ProfileSchoolViewModel? School { get; set; }
    }

    public sealed class ProfileQuestViewModel
    {
        /// <summary>avatar, firstName, lastName, currentStatusSentence, biography, skills or experience (the dashboard's codes).</summary>
        public string? Code { get; set; }

        public bool Done { get; set; }

        public int Percent { get; set; }
    }

    public sealed class PracticeStreakViewModel
    {
        public int Current { get; set; }

        public int Best { get; set; }

        public bool PractisedToday { get; set; }

        /// <summary>Oldest first, today last (UTC days).</summary>
        public IEnumerable<bool>? LastSevenDays { get; set; }
    }

    public sealed class ProfileSchoolViewModel
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        public int Reviews { get; set; }

        public int ReviewGoal { get; set; }

        public int Photos { get; set; }

        public int Students { get; set; }

        public int Teachers { get; set; }

        public int HelpersThisMonth { get; set; }

        /// <summary>The caller's own review: null (none or rejected), Review (waiting) or Confirmed.</summary>
        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? MyReview { get; set; }

        public long ReviewPoints { get; set; }

        public long PhotoPoints { get; set; }

        public long EditPoints { get; set; }
    }
}
