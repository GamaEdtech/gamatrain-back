namespace GamaEdtech.Application.Service
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text;

    using GamaEdtech.Data.Dto.WeeklyReport;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Builds the weekly report and quiet-week reminder emails as HTML. Email clients ignore stylesheets and most layout
    /// CSS, so it is tables with inline styles, 600px wide. Every text that comes from data (names, plan titles, school
    /// names) is HTML-encoded. Pure: no database, tested in WeeklyReportTests.
    /// </summary>
    internal static class WeeklyReportEmailBuilder
    {
        private const string Ink = "#1E2A44";
        private const string Muted = "#5F5B53";
        private const string Line = "#E7E4DC";
        private const string Tile = "#F7F6F2";
        private const string Brand = "#FFB300";
        private const string Bar = "#00695C";
        private const string BarTrack = "#ECE9E2";
        private const string TryNext = "#FFF6DD";
        private const string Link = "#00695C";

        /// <summary>The links every email carries; <see cref="Site"/> has no trailing slash.</summary>
        public sealed record Links(string Site, string Unsubscribe);

        public static WeeklyReportEmailDto Report(WeeklyReportDto report, Links links)
        {
            var body = new StringBuilder();
            _ = body.Append(Greeting(report.FirstName)).Append(Headline(ReportHeadline(report)));

            List<(string Label, string Value)> tiles =
            [
                ("Questions answered", N(report.QuestionsAnswered)),
                ("Correct", N(report.QuestionsCorrect)),
                ("Points earned", N(report.PointsEarned)),
            ];
            _ = body.Append(Tiles(tiles));

            if (report.Teacher is { } teacher)
            {
                _ = body.Append(Section("Your teaching this week"))
                    .Append(Tiles([("Paper downloads", N(teacher.PaperDownloads)), ("Joined by your invite", N(teacher.JoinedByInvite)), ("Posts published", N(teacher.PostsPublished))]))
                    .Append(Paragraph($"Your earnings are in your dashboard, not in this email. {Anchor($"{links.Site}/user/commission", "Open your earnings")}"));
            }

            if (report.Plan is { } plan)
            {
                _ = body.Append(PlanSection(plan, report.UsageThisWeek));
            }

            if (report.Contributions.Count > 0)
            {
                _ = body.Append(Section("Your contributions"));
                foreach (var item in report.Contributions)
                {
                    _ = body.Append(Row(Encode(ContributionText(item)), item.Status == Status.Confirmed ? "Approved" : StatusText(item.Status)));
                }
            }

            if (report.School is { } school)
            {
                _ = body.Append(SchoolSection(school, links));
            }
            else if (report.ShowAddSchool)
            {
                _ = body.Append(Paragraph($"{Anchor($"{links.Site}/user/profile", "Add your school in your profile")} to see how it compares with others in your city."));
            }

            var streakText = report.Streak > 0
                ? $"🔥 You're on a {report.Streak}-day streak. Keep it alive today."
                : "Answer one question a day and watch your streak grow 🔥";
            _ = body.Append(report.Teacher is not null && report.QuestionsAnswered == 0
                ? Callout("Next week's challenge", "Upload one more past paper. Every download adds to your earnings.", $"{links.Site}/user/paper/create", "Upload a paper")
                : Callout("Next week's challenge", streakText, $"{links.Site}/search?type=quizhub", "Practise on QuizHub"));

            var footer = $"You get this email because your GamaTrain plan includes the weekly report.<br>{Anchor($"{links.Site}/user/setting", "Email settings")} · {Anchor(links.Unsubscribe, "Stop weekly reports")}";
            return new()
            {
                Kind = WeeklyReportKind.Report,
                Subject = WeeklyReportRules.Subject(report),
                Html = Page(links.Site, $"Your week · {Range(report)}", body.ToString(), footer, $"{Range(report)}: {ReportHeadline(report)}"),
            };
        }

        /// <summary>
        /// The short email for a quiet week, as a challenge: one small goal to come back to (practice for a student, an upload
        /// for a teacher), then what is still waiting in the plan, as a share (quota units can be prices, so never raw numbers).
        /// </summary>
        public static WeeklyReportEmailDto Reminder(WeeklyReportDto report, Links links)
        {
            var teacher = report.IsTeacher;
            var body = new StringBuilder()
                .Append(Greeting(report.FirstName))
                .Append(Headline(teacher ? "Your next paper could be downloaded this week 📄" : "Ready for a comeback? 🎯"))
                .Append(Paragraph(teacher
                    ? "Last week was quiet. Students look for past papers on GamaTrain every day, and one upload puts yours in front of them."
                    : "Last week was quiet. One short session is all it takes to get going again, and every correct answer earns points."))
                .Append(teacher
                    ? Challenge(["Upload one past paper", "Add its subject and grade so students find it", "See its downloads in next week's report"], $"{links.Site}/user/paper/create", "Upload a paper")
                    : Challenge(["Answer 10 practice questions", "Get 7 of them right", "Come back the next day to start a streak 🔥"], $"{links.Site}/search?type=quizhub", "Start the challenge"));

            if (report.Plan is { Quotas.Count: > 0 } plan)
            {
                _ = body.Append(Section("Still waiting in your plan"));
                foreach (var quota in plan.Quotas)
                {
                    _ = body.Append(quota.Limit is { } limit
                        ? Meter(quota.Name, $"{N(limit - quota.Used)} of {N(limit)} left", Percent(limit - quota.Used, limit))
                        : Row(Encode(quota.Name), "Unlimited"));
                }
            }

            var footer = $"We send this at most once every 4 weeks, only after a week with no activity.<br>{Anchor($"{links.Site}/user/setting", "Email settings")} · {Anchor(links.Unsubscribe, "Stop these emails")}";
            return new()
            {
                Kind = WeeklyReportKind.Reminder,
                Subject = WeeklyReportRules.ReminderSubject(report),
                Html = Page(links.Site, "This week's challenge", body.ToString(), footer, teacher
                    ? "One upload this week, and next Monday's report shows who downloaded it."
                    : "10 questions, about 10 minutes. Can you get 7 right?"),
            };
        }

        private static string ReportHeadline(WeeklyReportDto report)
        {
            if (report.Teacher is { PaperDownloads: > 0 } teacher)
            {
                return $"Students downloaded your past papers {WeeklyReportRules.Times(teacher.PaperDownloads)} this week.";
            }

            if (report.QuestionsAnswered > 0)
            {
                return $"You answered {WeeklyReportRules.Plural(report.QuestionsAnswered, "practice question", "practice questions")} this week and got {report.QuestionsCorrect} right.";
            }

            var used = report.UsageThisWeek.Sum(t => t.Count);
            if (used > 0)
            {
                return $"You used your plan {WeeklyReportRules.Times(used)} this week.";
            }

            var helped = report.Contributions.Sum(t => t.Count);
            return helped > 0 ? $"You helped GamaTrain {WeeklyReportRules.Times(helped)} this week." : "Here's your week on GamaTrain.";
        }

        private static string PlanSection(WeeklyReportPlanDto plan, IReadOnlyList<WeeklyReportCountDto> usage)
        {
            // No renewal or end date: in a weekly email it reads as a reminder to cancel.
            var html = new StringBuilder().Append(Section($"Your plan{(string.IsNullOrWhiteSpace(plan.Title) ? string.Empty : $": {Encode(plan.Title)}")}"));
            if (usage.Count > 0)
            {
                _ = html.Append(Paragraph($"Used this week: {Encode(string.Join(", ", usage.Select(t => $"{t.Name} ({t.Count})")))}."));
            }

            foreach (var quota in plan.Quotas)
            {
                _ = html.Append(quota.Limit is { } limit
                    ? Meter(quota.Name, $"{N(quota.Used)} of {N(limit)} used", Percent(quota.Used, limit))
                    : Row(Encode(quota.Name), $"Unlimited · {N(quota.Used)} used"));
            }

            return html.ToString();
        }

        private static string SchoolSection(WeeklyReportSchoolDto school, Links links)
        {
            var html = new StringBuilder()
                .Append("<tr><td style=\"padding:24px 32px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#EEF5F3;border-radius:10px\"><tr><td style=\"padding:20px\">")
                .Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:12px;font-weight:600;color:{Muted}\">Your school</div>")
                .Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:17px;font-weight:600;color:{Ink};padding:2px 0 10px\">{Encode(school.Name ?? "Your school")}</div>");

            if (school.Rating is { } rating)
            {
                _ = html.Append(InlineMeter("Your school", $"{rating.ToString("0.0", CultureInfo.InvariantCulture)} out of 5 · {WeeklyReportRules.Plural(school.Reviews, "review", "reviews")}", (int)Math.Round(rating * 20), Bar));
                if (school.CityAverage is { } average)
                {
                    _ = html.Append(InlineMeter($"Average of rated schools in {Encode(school.CityName ?? "your city")}", $"{average.ToString("0.0", CultureInfo.InvariantCulture)} out of 5", (int)Math.Round(average * 20), "#7A9C95"));
                }
            }
            else
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:14px;color:{Ink};padding-bottom:8px\">No reviews yet. Be the first to review your school.</div>");
            }

            List<string> changes = [];
            if (school.NewReviews > 0)
            {
                changes.Add(WeeklyReportRules.Plural(school.NewReviews, "new review", "new reviews"));
            }

            if (school.NewPhotos > 0)
            {
                changes.Add(WeeklyReportRules.Plural(school.NewPhotos, "new photo", "new photos"));
            }

            if (school.NewMembers > 0)
            {
                changes.Add(WeeklyReportRules.Plural(school.NewMembers, "new member", "new members"));
            }

            var week = changes.Count > 0 ? $"This week: {string.Join(", ", changes)}. " : string.Empty;
            _ = html.Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:13px;line-height:1.5;color:{Muted};padding-top:4px\">{week}{Anchor($"{links.Site}/school/{school.Id}", "Open your school")}</div>")
                .Append("</td></tr></table></td></tr>");
            return html.ToString();
        }

        private static string ContributionText(WeeklyReportContributionDto item)
        {
            var (one, many) = item.Category switch
            {
                _ when item.Category == CategoryType.School => ("school edit", "school edits"),
                _ when item.Category == CategoryType.SchoolImage => ("school photo", "school photos"),
                _ when item.Category == CategoryType.SchoolComment => ("school review", "school reviews"),
                _ when item.Category == CategoryType.SchoolIssues => ("school problem report", "school problem reports"),
                _ when item.Category == CategoryType.RemoveSchoolImage => ("photo removal", "photo removals"),
                _ when item.Category == CategoryType.Post => ("post", "posts"),
                _ when item.Category == CategoryType.PostComment => ("comment", "comments"),
                _ => ("contribution", "contributions"),
            };
            return WeeklyReportRules.Plural(item.Count, one, many);
        }

        private static string StatusText(Status status) => status == Status.Rejected ? "Not accepted" : "Waiting for a check";

        /// <summary>
        /// The page; <paramref name="preheader"/> is the hidden line inboxes show after the subject. The logo is the site's own
        /// PNG (email clients don't show SVG), with "GamaTrain" as its alt text for clients that block images.
        /// </summary>
        private static string Page(string site, string topRight, string body, string footer, string preheader) =>
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>"
            + "<body style=\"margin:0;padding:0;background:#F3F1EC\">"
            + "<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;font-size:1px;line-height:1px;color:#F3F1EC\">" + Encode(preheader) + "</div>"
            + "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F3F1EC\"><tr><td align=\"center\" style=\"padding:24px 12px\">"
            + "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;font-family:Arial,Helvetica,sans-serif;color:" + Ink + "\">"
            + "<tr><td style=\"padding:0 8px 16px\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>"
            + "<td style=\"font-size:20px;font-weight:bold\"><a href=\"" + Attribute(site) + "\" style=\"text-decoration:none;color:" + Ink + "\"><img src=\"" + Attribute($"{site}/images/gamatrain-logo-black.png") + "\" width=\"106\" height=\"32\" alt=\"GamaTrain\" style=\"display:block;border:0\"></a></td>"
            + "<td align=\"right\" style=\"font-size:13px;color:" + Muted + "\">" + topRight + "</td></tr></table></td></tr>"
            + "<tr><td style=\"background:#FFFFFF;border-radius:12px;overflow:hidden\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">"
            + "<tr><td style=\"height:6px;background:" + Brand + ";font-size:0;line-height:0\">&nbsp;</td></tr>"
            + body
            + "<tr><td style=\"height:32px;font-size:0;line-height:0\">&nbsp;</td></tr></table></td></tr>"
            + "<tr><td align=\"center\" style=\"padding:20px 24px 0;font-size:12px;line-height:1.6;color:" + Muted + "\">" + footer + "</td></tr>"
            + "</table></td></tr></table></body></html>";

        private static string Greeting(string? firstName) =>
            $"<tr><td style=\"padding:32px 32px 0;font-size:14px;color:{Muted}\">Hi {Encode(string.IsNullOrWhiteSpace(firstName) ? "there" : firstName.Trim())},</td></tr>";

        private static string Headline(string text) =>
            $"<tr><td style=\"padding:8px 32px 0;font-size:24px;line-height:1.25;font-weight:bold;color:{Ink}\">{text}</td></tr>";

        private static string Tiles(IEnumerable<(string Label, string Value)> tiles) =>
            "<tr><td style=\"padding:20px 26px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>"
            + string.Concat(tiles.Select(t => $"<td width=\"33%\" style=\"padding:0 6px\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Tile};border-radius:10px\"><tr><td style=\"padding:14px\"><div style=\"font-size:12px;color:{Muted}\">{t.Label}</div><div style=\"font-size:26px;font-weight:bold;color:{Ink};padding-top:4px\">{t.Value}</div></td></tr></table></td>"))
            + "</tr></table></td></tr>";

        private static string Section(string title, string aside = "") =>
            $"<tr><td style=\"padding:28px 32px 6px\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-top:1px solid {Line}\"><tr><td style=\"padding-top:20px;font-size:15px;font-weight:bold;color:{Ink}\">{title}</td><td align=\"right\" style=\"padding-top:20px;font-size:13px;color:{Muted}\">{aside}</td></tr></table></td></tr>";

        private static string Paragraph(string html) =>
            $"<tr><td style=\"padding:6px 32px 0;font-size:13px;line-height:1.5;color:{Muted}\">{html}</td></tr>";

        private static string Row(string left, string right) =>
            $"<tr><td style=\"padding:6px 32px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td style=\"font-size:14px;color:{Ink}\">{left}</td><td align=\"right\" style=\"font-size:13px;color:{Muted}\">{right}</td></tr></table></td></tr>";

        private static string Meter(string name, string value, int percent) =>
            $"<tr><td style=\"padding:8px 32px 0\">{InlineMeter(Encode(name), value, percent, Bar)}</td></tr>";

        private static string InlineMeter(string label, string value, int percent, string color)
        {
            var width = Math.Clamp(percent, 0, 100);
            var fill = width == 0 ? string.Empty : $"<td width=\"{width}%\" style=\"background:{color};height:8px;font-size:0;line-height:0\">&nbsp;</td>";
            var rest = width == 100 ? string.Empty : $"<td style=\"height:8px;font-size:0;line-height:0\">&nbsp;</td>";
            return $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-bottom:8px\"><tr><td style=\"font-size:13px;color:{Ink};padding-bottom:4px\">{label}</td><td align=\"right\" style=\"font-size:13px;color:{Muted};padding-bottom:4px\">{value}</td></tr>"
                + $"<tr><td colspan=\"2\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{BarTrack};border-radius:4px\"><tr>{fill}{rest}</tr></table></td></tr></table>";
        }

        private static string Callout(string eyebrow, string text, string url, string button) =>
            $"<tr><td style=\"padding:24px 32px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{TryNext};border-radius:10px\"><tr><td style=\"padding:20px\">"
            + $"<div style=\"font-size:12px;font-weight:600;color:{Muted}\">{eyebrow}</div>"
            + $"<div style=\"font-size:16px;font-weight:600;line-height:1.35;color:{Ink};padding:6px 0 14px\">{text}</div>"
            + $"<a href=\"{Attribute(url)}\" style=\"display:inline-block;background:{Brand};color:{Ink};font-size:15px;font-weight:bold;text-decoration:none;padding:12px 22px;border-radius:8px\">{button}</a>"
            + "</td></tr></table></td></tr>";

        /// <summary>A goal card: a short checklist and one button. The boxes are drawn, so they show in every client.</summary>
        private static string Challenge(IEnumerable<string> steps, string url, string button) =>
            $"<tr><td style=\"padding:24px 32px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{TryNext};border-radius:10px\"><tr><td style=\"padding:20px\">"
            + $"<div style=\"font-size:12px;font-weight:600;color:{Muted};padding-bottom:8px\">This week's challenge</div>"
            + string.Concat(steps.Select(t => $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr><td style=\"padding:5px 10px 5px 0;vertical-align:middle\"><div style=\"width:14px;height:14px;border:2px solid {Ink};border-radius:4px\"></div></td><td style=\"padding:5px 0;font-size:15px;font-weight:600;color:{Ink}\">{Encode(t)}</td></tr></table>"))
            + $"<div style=\"padding-top:14px\"><a href=\"{Attribute(url)}\" style=\"display:inline-block;background:{Brand};color:{Ink};font-size:15px;font-weight:bold;text-decoration:none;padding:12px 22px;border-radius:8px\">{button}</a></div>"
            + "</td></tr></table></td></tr>";

        private static string Anchor(string url, string text) => $"<a href=\"{Attribute(url)}\" style=\"color:{Link}\">{text}</a>";

        private static string Range(WeeklyReportDto report)
        {
            var last = report.WeekEnd.AddDays(-1);
            return report.WeekStart.Month == last.Month
                ? $"{report.WeekStart.Day}–{Day(last)}"
                : $"{Day(report.WeekStart)} – {Day(last)}";
        }

        private static int Percent(int part, int whole) => whole <= 0 ? 0 : part * 100 / whole;

        private static string Day(DateTimeOffset date) => date.ToString("d MMM", CultureInfo.InvariantCulture);

        private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        private static string Attribute(string url) => WebUtility.HtmlEncode(url);
    }
}
