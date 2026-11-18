namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;


    using GamaEdtech.Data.Dto.WeeklyReport;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The pure rules of the weekly report: which week, when there is something to report, when a reminder goes.</summary>
    internal static class WeeklyReportRules
    {
        /// <summary>A quiet week gets a reminder only when the last one was at least this long ago.</summary>
        public const int ReminderIntervalDays = 28;

        /// <summary>The "add your school" line is shown in this many reports, then dropped so it doesn't nag.</summary>
        public const int AddSchoolReports = 3;

        /// <summary>At most this many exams are listed in one report.</summary>
        public const int MaxExamsShown = 5;

        /// <summary>An exam title longer than this is cut in a subject line.</summary>
        private const int MaxSubjectTitle = 40;

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
            || report.Exams.Count > 0
            || report.PostsRead > 0 || report.PodcastsPlayed > 0
            || report.UsageThisWeek.Any(t => t.Count > 0)
            || report.Contributions.Any(t => t.Count > 0)
            || report.Teacher is { PaperDownloads: > 0 } or { JoinedByInvite: > 0 } or { PostsPublished: > 0 } or { PostReaders: > 0 } or { InvitedBought: > 0 } or { ExamTakers: > 0 };

        /// <summary>
        /// A quiet week gets the short reminder only when the plan still has something left to use and no reminder went
        /// for one of the last <see cref="ReminderIntervalDays"/> days' weeks: so a quiet user hears from us once every 4 weeks
        /// at most. <paramref name="lastReminderWeek"/> is the week the last reminder was about.
        /// </summary>
        public static bool ShouldRemind(WeeklyReportPlanDto? plan, DateTimeOffset? lastReminderWeek, DateTimeOffset weekStart) =>
            plan is not null
            && plan.Quotas.Any(t => t.Limit is null || t.Used < t.Limit)
            && (lastReminderWeek is null || lastReminderWeek.Value <= weekStart.AddDays(-ReminderIntervalDays));

        /// <summary>
        /// The report's subject: its strongest number, led by an emoji badge, so the inbox line alone feels like a result worth
        /// opening. Only real numbers from the week; nothing is invented to sound bigger.
        /// </summary>
        public static string Subject(WeeklyReportDto report)
        {
            if (report.Teacher is { PaperDownloads: > 0 } teacher)
            {
                return teacher.PostReaders > 0
                    ? $"📈 Your papers were downloaded {Times(teacher.PaperDownloads)}, and {People(teacher.PostReaders)} read your posts"
                    : $"📈 Your papers were downloaded {Times(teacher.PaperDownloads)} this week";
            }

            if (BestExam(report) is { Percent: { } best } exam)
            {
                return $"📝 You scored {Score(best)} on {ShortTitle(exam.Title)}. Beat it next week?";
            }

            if (report.Teacher is { PostReaders: > 0 } readers)
            {
                return $"📖 {People(readers.PostReaders)} read your posts this week";
            }

            if (report.QuestionsAnswered > 0)
            {
                return report.Streak >= 2
                    ? $"🔥 {report.Streak}-day streak! {Plural(report.QuestionsAnswered, "question", "questions")} this week"
                    : $"🎯 {report.QuestionsCorrect} of {report.QuestionsAnswered} right this week. Can you beat it?";
            }

            if (report.Teacher is { InvitedBought: > 0 } invites)
            {
                return $"🎉 {People(invites.InvitedBought)} you invited bought a plan";
            }

            if (report.PointsEarned > 0)
            {
                return $"⭐ You earned {report.PointsEarned.ToString("N0", CultureInfo.InvariantCulture)} points this week";
            }

            if (report.PostsRead > 0)
            {
                return $"📚 You read {Plural(report.PostsRead, "article", "articles")} this week";
            }

            var approved = report.Contributions.Where(t => t.Status == Status.Confirmed).Sum(t => t.Count);
            return approved > 0
                ? $"🏅 {Plural(approved, "contribution", "contributions")} approved this week"
                : "📬 Your week on GamaTrain";
        }

        /// <summary>
        /// The inbox preview line after the subject: what else the week held (a teacher's invite purchases first), or null to
        /// fall back to the headline.
        /// </summary>
        public static string? Preheader(WeeklyReportDto report)
        {
            if (report.Teacher is { InvitedBought: > 0 } teacher)
            {
                return $"{People(teacher.InvitedBought)} you invited bought a plan 🎉";
            }

            List<string> parts = [];
            if (report.Exams.Count > 0)
            {
                parts.Add(Plural(report.Exams.Count, "exam", "exams"));
            }

            if (report.PostsRead > 0)
            {
                parts.Add(Plural(report.PostsRead, "article", "articles"));
            }

            if (report.PodcastsPlayed > 0)
            {
                parts.Add(Plural(report.PodcastsPlayed, "podcast", "podcasts"));
            }

            if (report.Streak >= 2)
            {
                parts.Add($"a {report.Streak}-day streak");
            }

            return parts.Count == 0 ? null : $"{JoinLabels(parts)} this week.";
        }

        /// <summary>The week's best-scored exam (gama-api's percent), or null when none has a score.</summary>
        public static WeeklyReportExamDto? BestExam(WeeklyReportDto report) =>
            report.Exams.Where(t => t.Percent is not null).OrderByDescending(t => t.Percent).FirstOrDefault();

        /// <summary>A score as people read it: "85%", "72.5%".</summary>
        public static string Score(decimal percent) => $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%";

        /// <summary>An exam title short enough for a subject line.</summary>
        public static string ShortTitle(string? title)
        {
            var text = string.IsNullOrWhiteSpace(title) ? "an exam" : title.Trim();
            return text.Length <= MaxSubjectTitle ? text : $"{text[..(MaxSubjectTitle - 1)].TrimEnd()}…";
        }

        public static string People(int count) => Plural(count, "person", "people");

        /// <summary>
        /// The quiet-week reminder's subject: a nudge back, never a renewal or end date (a date there reads as "time to
        /// cancel").
        /// </summary>
        public static string ReminderSubject(WeeklyReportDto report) => report.IsTeacher
            ? "📄 Students are looking for papers. Share one this week"
            : "🎯 Ready for a comeback? Start a new streak today";

        /// <summary>
        /// A feature's name for people: the built-in features by code, anything else by its catalog name. The catalog's names
        /// are code-like ("ExamDownload"), so they are never shown as they are for a known code.
        /// </summary>
        public static string FeatureLabel(string? code, string? name) => code switch
        {
            FeatureCodes.PastpaperDownload => "past papers",
            FeatureCodes.TestDownload => "test downloads",
            FeatureCodes.TestSubmission => "test submissions",
            FeatureCodes.ExamParticipation => "exams",
            FeatureCodes.MultimediaDownload => "multimedia",
            FeatureCodes.ExamDownload => "exam downloads",
            FeatureCodes.ExamAnalysis => "exam analysis",
            null => name ?? "other",
            _ => name ?? code,
        };

        /// <summary>"a", "a and b", "a, b and c", with the first letter capitalised.</summary>
        public static string JoinLabels(IReadOnlyList<string> labels)
        {
            var text = labels.Count <= 1 ? string.Concat(labels) : $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}";
            return text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]);
        }

        public static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

        public static string Times(int count) => count == 1 ? "once" : $"{count} times";
    }
}
