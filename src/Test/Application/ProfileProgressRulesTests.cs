namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Domain.Enumeration;

    using Xunit;

    /// <summary>The profile level and the progress rules behind <c>GET identities/profiles/progress</c>. Pure, no database.</summary>
    public class ProfileProgressRulesTests
    {
        private const string Status = "IGCSE in June, aiming for an A";
        private const string Biography = "I like physics and maths, and I am preparing for my IGCSE exams this year.";

        [Fact]
        public void EmptyProfileScoresZero() =>
            Assert.Equal(0, UserRateLevel.Score(null, null, null, null, null, null, null));

        // Until 2026-11-13 a missing photo or name added its weight (an empty profile was 25%, a full one 75%).
        [Fact]
        public void PhotoAndNamesCountOnlyWhenFilled()
        {
            Assert.Equal(15, UserRateLevel.Score("avatar.png", null, null, null, null, null, null));
            Assert.Equal(25, UserRateLevel.Score("avatar.png", "Sara", "Karimi", null, null, null, null));
        }

        [Fact]
        public void FullProfileScoresOneHundred() =>
            Assert.Equal(100, UserRateLevel.Score("avatar.png", "Sara", "Karimi", Status, Biography, ["Physics"], [1]));

        [Theory]
        [InlineData("short", 0)]
        [InlineData("ten chars!", 10)]
        public void StatusCountsWhenLongerThanNine(string status, int expected) =>
            Assert.Equal(expected, UserRateLevel.Score(null, null, null, status, null, null, null));

        [Fact]
        public void BlankSkillsDoNotCount() =>
            Assert.Equal(0, UserRateLevel.Score(null, null, null, null, null, ["", "  "], null));

        // Until 2026-11-13 a score of 60-79 was Intermediate, so Advanced was never reached.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(19, 0)]
        [InlineData(20, 1)]
        [InlineData(40, 2)]
        [InlineData(59, 2)]
        [InlineData(60, 3)]
        [InlineData(79, 3)]
        [InlineData(80, 4)]
        [InlineData(100, 4)]
        public void LevelFollowsThresholds(int percent, byte level) =>
            Assert.Equal(level, UserRateLevel.Calculate(percent).Value);

        [Fact]
        public void QuestsAreEveryWeightedFieldAndAddUpToOneHundred()
        {
            var quests = ProfileProgressRules.Quests(null, null, null, null, null, null, false);

            Assert.Equal(["avatar", "firstName", "lastName", "currentStatusSentence", "biography", "skills", "experience"], quests.Select(t => t.Code));
            Assert.Equal(100, quests.Sum(t => t.Percent));
            Assert.All(quests, t => Assert.False(t.Done));
        }

        [Fact]
        public void QuestsMatchTheScore()
        {
            var quests = ProfileProgressRules.Quests("avatar.png", "Sara", null, Status, "too short", ["Physics"], false);

            Assert.Equal(
                UserRateLevel.Score("avatar.png", "Sara", null, Status, "too short", ["Physics"], null),
                quests.Where(t => t.Done).Sum(t => t.Percent));
            Assert.Equal(["lastName", "biography", "experience"], quests.Where(t => !t.Done).Select(t => t.Code));
        }

        [Theory]
        [InlineData(0, 1, 20)]
        [InlineData(45, 3, 60)]
        [InlineData(79, 4, 80)]
        public void NextLevelIsTheFollowingThreshold(int percent, byte level, int atPercent)
        {
            var (next, at) = ProfileProgressRules.NextLevel(percent);

            Assert.Equal(level, next!.Value);
            Assert.Equal(atPercent, at);
        }

        [Fact]
        public void CompleteHasNoNextLevel() =>
            Assert.Equal((null, null), ProfileProgressRules.NextLevel(80));

        [Theory]
        [InlineData(0, 5)]
        [InlineData(4, 5)]
        [InlineData(5, 10)]
        [InlineData(38, 50)]
        [InlineData(50, 100)]
        [InlineData(999, 1000)]
        [InlineData(1000, 2000)]
        [InlineData(2500, 3000)]
        public void GoalIsTheNextMilestone(int count, int goal) =>
            Assert.Equal(goal, ProfileProgressRules.NextGoal(count));

        private static readonly DateOnly Today = new(2026, 11, 13);

        [Fact]
        public void NoPracticeIsNoStreak()
        {
            var streak = ProfileProgressRules.Streak([], Today);

            Assert.Equal(0, streak.Current);
            Assert.Equal(0, streak.Best);
            Assert.False(streak.PractisedToday);
            Assert.Equal(7, streak.LastSevenDays.Count);
            Assert.DoesNotContain(true, streak.LastSevenDays);
        }

        [Fact]
        public void StreakCountsDaysInARowEndingToday()
        {
            var streak = ProfileProgressRules.Streak([Today, Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-4)], Today);

            Assert.Equal(3, streak.Current);
            Assert.True(streak.PractisedToday);
            Assert.Equal([false, false, true, false, true, true, true], streak.LastSevenDays);
        }

        // Not practised yet today: the streak isn't lost until the day is over.
        [Fact]
        public void StreakEndingYesterdayIsStillCurrent()
        {
            var streak = ProfileProgressRules.Streak([Today.AddDays(-1), Today.AddDays(-2)], Today);

            Assert.Equal(2, streak.Current);
            Assert.False(streak.PractisedToday);
        }

        [Fact]
        public void AGapEndsTheStreak() =>
            Assert.Equal(0, ProfileProgressRules.Streak([Today.AddDays(-2), Today.AddDays(-3)], Today).Current);

        [Fact]
        public void BestIsTheLongestRunEver()
        {
            DateOnly[] days = [new(2026, 3, 1), new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 4), new(2026, 3, 10), Today];

            var streak = ProfileProgressRules.Streak(days, Today);

            Assert.Equal(4, streak.Best);
            Assert.Equal(1, streak.Current);
        }

        [Fact]
        public void FutureDaysAreIgnored() =>
            Assert.Equal(0, ProfileProgressRules.Streak([Today.AddDays(1)], Today).Best);
    }
}
