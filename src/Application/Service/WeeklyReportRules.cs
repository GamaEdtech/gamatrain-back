namespace GamaEdtech.Application.Service
{
    using System;
    using System.Linq;

    using GamaEdtech.Data.Dto.WeeklyReport;

    /// <summary>The pure rules of the weekly report: which week, when there is something to report, when a reminder goes.</summary>
    internal static class WeeklyReportRules
    {
        /// <summary>A quiet week gets a reminder only when the last one was at least this long ago.</summary>
        public const int ReminderIntervalDays = 28;

        /// <summary>The "add your school" line is shown in this many reports, then dropped so it doesn't nag.</summary>
        public const int AddSchoolReports = 3;

        /// <summary>Monday 00:00 UTC of the last full week before <paramref name="now"/>: on a Monday, the week that just ended.</summary>
        public static DateTimeOffset LastFullWeek(DateTimeOffset now)
        {
            var today = now.UtcDateTime.Date;
            var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return new DateTimeOffset(thisMonday.AddDays(-7), TimeSpan.Zero);
        }

        /// <summary>Monday 00:00 UTC of the week holding <paramref name="day"/>.</summary>
        public static DateTimeOffset WeekOf(DateOnly day)
        {
            var date = day.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), TimeSpan.Zero);
        }

        /// <summary>Anything the user did this week that the report can show.</summary>
        public static bool HasActivity(WeeklyReportDto report) =>
            report.QuestionsAnswered > 0
            || report.UsageThisWeek.Any(t => t.Count > 0)
            || report.Contributions.Any(t => t.Count > 0)
            || report.Teacher is { PaperDownloads: > 0 } or { JoinedByInvite: > 0 } or { PostsPublished: > 0 };

        /// <summary>
        /// A quiet week gets the short reminder only when the plan still has something left to use and no reminder went
        /// for one of the last <see cref="ReminderIntervalDays"/> days' weeks: so a quiet user hears from us once every 4 weeks
        /// at most. <paramref name="lastReminderWeek"/> is the week the last reminder was about.
        /// </summary>
        public static bool ShouldRemind(WeeklyReportPlanDto? plan, DateTimeOffset? lastReminderWeek, DateTimeOffset weekStart) =>
            plan is not null
            && plan.Quotas.Any(t => t.Limit is null || t.Used < t.Limit)
            && (lastReminderWeek is null || lastReminderWeek.Value <= weekStart.AddDays(-ReminderIntervalDays));

        /// <summary>The report's subject: its strongest number, so the inbox line alone says something.</summary>
        public static string Subject(WeeklyReportDto report)
        {
            if (report.Teacher is { PaperDownloads: > 0 } teacher)
            {
                return $"Your week on GamaTrain: your papers were downloaded {Times(teacher.PaperDownloads)}";
            }

            if (report.QuestionsAnswered > 0)
            {
                return $"Your week on GamaTrain: {Plural(report.QuestionsAnswered, "practice question", "practice questions")}, {report.QuestionsCorrect} right";
            }

            var approved = report.Contributions.Where(t => t.Status == Domain.Enumeration.Status.Confirmed).Sum(t => t.Count);
            return approved > 0
                ? $"Your week on GamaTrain: {Plural(approved, "contribution", "contributions")} approved"
                : "Your week on GamaTrain";
        }

        public static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

        public static string Times(int count) => count == 1 ? "once" : $"{count} times";
    }
}
