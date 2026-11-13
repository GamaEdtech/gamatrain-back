namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using GamaEdtech.Data.Dto.ProfileProgress;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The pure rules behind <see cref="ProfileProgressService"/>: quests, level, practice streak, school goal.</summary>
    internal static class ProfileProgressRules
    {
        /// <summary>Review goals a school works towards; past the last one, every next 1000.</summary>
        private static readonly int[] Milestones = [5, 10, 25, 50, 100, 250, 500, 1000];

        /// <summary>The profile fields as quests, in the order they are worth doing: cheap first, then the big ones.</summary>
        public static IReadOnlyList<ProfileQuestDto> Quests(string? avatar, string? firstName, string? lastName, string? status,
            string? biography, IEnumerable<string?>? skills, bool hasExperience) =>
            [
                new() { Code = "avatar", Done = !string.IsNullOrEmpty(avatar), Percent = UserRateLevel.AvatarWeight },
                new() { Code = "firstName", Done = !string.IsNullOrEmpty(firstName), Percent = UserRateLevel.FirstNameWeight },
                new() { Code = "lastName", Done = !string.IsNullOrEmpty(lastName), Percent = UserRateLevel.LastNameWeight },
                new() { Code = "currentStatusSentence", Done = status?.Length > UserRateLevel.MinStatusLength, Percent = UserRateLevel.StatusWeight },
                new() { Code = "biography", Done = biography?.Length > UserRateLevel.MinBiographyLength, Percent = UserRateLevel.BiographyWeight },
                new() { Code = "skills", Done = skills?.Any(t => !string.IsNullOrWhiteSpace(t)) == true, Percent = UserRateLevel.SkillsWeight },
                new() { Code = "experience", Done = hasExperience, Percent = UserRateLevel.ExperienceWeight },
            ];

        /// <summary>The level after <paramref name="percent"/>'s and where it starts; null at the top.</summary>
        public static (UserRateLevel? Level, int? AtPercent) NextLevel(int percent)
        {
            foreach (var (level, atPercent) in UserRateLevel.Thresholds)
            {
                if (percent < atPercent)
                {
                    return (level, atPercent);
                }
            }

            return (null, null);
        }

        /// <summary>The first milestone above <paramref name="count"/>: 0 → 5, 5 → 10, 38 → 50, 1000 → 2000.</summary>
        public static int NextGoal(int count) =>
            Milestones.FirstOrDefault(t => count < t) is var milestone and > 0 ? milestone : ((count / 1000) + 1) * 1000;

        /// <summary>
        /// The streak from the days with practice. The current streak ends today, or yesterday while today has none yet
        /// (so it isn't shown as lost before the day is over); a gap before that ends it.
        /// </summary>
        public static PracticeStreakDto Streak(IEnumerable<DateOnly> days, DateOnly today)
        {
            var set = days.Where(t => t <= today).ToHashSet();

            var current = 0;
            var day = set.Contains(today) ? today : today.AddDays(-1);
            while (set.Contains(day))
            {
                current++;
                day = day.AddDays(-1);
            }

            var best = 0;
            var run = 0;
            DateOnly? previous = null;
            foreach (var t in set.Order())
            {
                run = previous == t.AddDays(-1) ? run + 1 : 1;
                best = Math.Max(best, run);
                previous = t;
            }

            return new()
            {
                Current = current,
                Best = best,
                PractisedToday = set.Contains(today),
                LastSevenDays = [.. Enumerable.Range(0, 7).Select(t => set.Contains(today.AddDays(t - 6)))],
            };
        }
    }
}
