namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.WeeklyReport;
    using GamaEdtech.Domain.Enumeration;

    using Xunit;

    /// <summary>The weekly report's rules and email. Pure, no database or email sent.</summary>
    public class WeeklyReportTests
    {
        private static readonly DateTimeOffset Monday = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        private static readonly WeeklyReportEmailBuilder.Links Links = new("https://gamatrain.com", "https://gamatrain.com/unsubscribe?type=weekly-report&userId=7&token=abc%2Bdef");

        [Theory]
        [InlineData("2026-11-09T06:00:00Z", "2026-11-02")]
        [InlineData("2026-11-09T23:59:00Z", "2026-11-02")]
        [InlineData("2026-11-15T12:00:00Z", "2026-11-02")]
        [InlineData("2026-11-16T00:00:00Z", "2026-11-09")]
        [InlineData("2026-01-05T06:00:00+03:30", "2025-12-29")]
        public void LastFullWeekIsTheMondayBefore(string now, string expected) =>
            Assert.Equal(DateTimeOffset.Parse(expected + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                WeeklyReportRules.LastFullWeek(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

        [Theory]
        [InlineData("2026-11-02", "2026-11-02")]
        [InlineData("2026-11-08", "2026-11-02")]
        [InlineData("2026-11-05", "2026-11-02")]
        public void WeekOfIsItsMonday(string day, string monday) =>
            Assert.Equal(new DateTimeOffset(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                WeeklyReportRules.WeekOf(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)));

        [Fact]
        public void EmptyWeekHasNoActivity() => Assert.False(WeeklyReportRules.HasActivity(Report()));

        [Fact]
        public void PracticeUsageContributionsAndTeachingAreActivity()
        {
            Assert.True(WeeklyReportRules.HasActivity(Report(r => r.QuestionsAnswered = 1)));
            Assert.True(WeeklyReportRules.HasActivity(Report(r => r.UsageThisWeek = [new() { Name = "Exam downloads", Count = 1 }])));
            Assert.True(WeeklyReportRules.HasActivity(Report(r => r.Contributions = [new() { Category = CategoryType.SchoolImage, Status = Status.Review, Count = 1 }])));
            Assert.True(WeeklyReportRules.HasActivity(Report(r => r.Teacher = new() { JoinedByInvite = 1 })));
            Assert.False(WeeklyReportRules.HasActivity(Report(r => r.Teacher = new())));
        }

        // A plan with something left, no reminder in the last 4 weeks.
        [Theory]
        [InlineData(null, true)]
        [InlineData("2026-10-05", true)]
        [InlineData("2026-10-12", false)]
        [InlineData("2026-10-26", false)]
        public void ReminderAtMostEveryFourWeeks(string? lastReminderWeek, bool expected) =>
            Assert.Equal(expected, WeeklyReportRules.ShouldRemind(Plan(used: 2, limit: 10),
                lastReminderWeek is null ? null : DateTimeOffset.Parse(lastReminderWeek + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Monday));

        [Fact]
        public void NoReminderWithoutAPlanOrWithNothingLeft()
        {
            Assert.False(WeeklyReportRules.ShouldRemind(null, null, Monday));
            Assert.False(WeeklyReportRules.ShouldRemind(Plan(used: 10, limit: 10), null, Monday));
            Assert.True(WeeklyReportRules.ShouldRemind(Plan(used: 50, limit: null), null, Monday));
        }

        [Fact]
        public void SubjectLeadsWithTheStrongestNumber()
        {
            Assert.Equal("Your week on GamaTrain: 23 practice questions, 17 right", WeeklyReportRules.Subject(Report(r => { r.QuestionsAnswered = 23; r.QuestionsCorrect = 17; })));
            Assert.Equal("Your week on GamaTrain: your papers were downloaded once", WeeklyReportRules.Subject(Report(r => { r.QuestionsAnswered = 5; r.Teacher = new() { PaperDownloads = 1 }; })));
            Assert.Equal("Your week on GamaTrain: 2 contributions approved", WeeklyReportRules.Subject(Report(r => r.Contributions = [new() { Category = CategoryType.School, Status = Status.Confirmed, Count = 2 }])));
            Assert.Equal("Your week on GamaTrain", WeeklyReportRules.Subject(Report()));
        }

        [Fact]
        public void ReportEncodesEverythingFromData()
        {
            var email = WeeklyReportEmailBuilder.Report(Report(r =>
            {
                r.FirstName = "<script>alert(1)</script>";
                r.School = new() { Id = 5, Name = "A & B <School>", CityName = "\"Nai\"robi" };
                r.Plan = new() { Title = "Pro <b>", Quotas = [new() { Name = "Exam <downloads>", Used = 1, Limit = 3 }] };
            }), Links);

            Assert.DoesNotContain("<script>", email.Html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", email.Html, StringComparison.Ordinal);
            Assert.Contains("A &amp; B &lt;School&gt;", email.Html, StringComparison.Ordinal);
            Assert.Contains("Pro &lt;b&gt;", email.Html, StringComparison.Ordinal);
            Assert.Contains("Exam &lt;downloads&gt;", email.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void ReportCarriesTheWeekTheNumbersAndTheLinks()
        {
            var email = WeeklyReportEmailBuilder.Report(Report(r =>
            {
                r.FirstName = "Sara";
                r.QuestionsAnswered = 23;
                r.QuestionsCorrect = 17;
                r.PointsEarned = 1250;
                r.Plan = new() { Title = "Pro", PeriodEnd = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero), Renews = true, Quotas = [new() { Name = "Past paper downloads", Used = 12, Limit = 30 }, new() { Name = "Exam analyses", Used = 2, Limit = null }] };
                r.UsageThisWeek = [new() { Name = "Past paper downloads", Count = 3 }];
            }), Links);

            Assert.Equal(WeeklyReportKind.Report, email.Kind);
            Assert.Contains("Your week · 2–8 Nov", email.Html, StringComparison.Ordinal);
            Assert.Contains("Hi Sara,", email.Html, StringComparison.Ordinal);
            Assert.Contains("You answered 23 practice questions this week and got 17 right.", email.Html, StringComparison.Ordinal);
            Assert.Contains("1,250", email.Html, StringComparison.Ordinal);
            Assert.Contains("Renews 24 Nov", email.Html, StringComparison.Ordinal);
            Assert.Contains("12 of 30 used", email.Html, StringComparison.Ordinal);
            Assert.Contains("Unlimited · 2 used", email.Html, StringComparison.Ordinal);
            Assert.Contains("Used this week: Past paper downloads (3).", email.Html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://gamatrain.com/unsubscribe?type=weekly-report&amp;userId=7&amp;token=abc%2Bdef\"", email.Html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://gamatrain.com/user/setting\"", email.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void TeacherPartLinksToEarningsAndShowsNoAmount()
        {
            var email = WeeklyReportEmailBuilder.Report(Report(r => r.Teacher = new() { PaperDownloads = 14, JoinedByInvite = 2, PostsPublished = 1 }), Links);

            Assert.Contains("Students downloaded your past papers 14 times this week.", email.Html, StringComparison.Ordinal);
            Assert.Contains("/user/commission", email.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("$", email.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void SchoolComparesOnThePublicRating()
        {
            var rated = WeeklyReportEmailBuilder.Report(Report(r => r.School = new() { Id = 9, Name = "Greenfield", Rating = 4.2, Reviews = 38, CityName = "Nairobi", CityAverage = 3.8, NewReviews = 3 }), Links).Html;
            Assert.Contains("4.2 out of 5 · 38 reviews", rated, StringComparison.Ordinal);
            Assert.Contains("Average of rated schools in Nairobi", rated, StringComparison.Ordinal);
            Assert.Contains("This week: 3 new reviews.", rated, StringComparison.Ordinal);
            Assert.Contains("/school/9", rated, StringComparison.Ordinal);

            var unrated = WeeklyReportEmailBuilder.Report(Report(r => r.School = new() { Id = 9, Name = "Greenfield" }), Links).Html;
            Assert.Contains("No reviews yet. Be the first to review your school.", unrated, StringComparison.Ordinal);
        }

        [Fact]
        public void AddSchoolLineOnlyWhenAskedAndThereIsNoSchool()
        {
            Assert.Contains("Add your school in your profile", WeeklyReportEmailBuilder.Report(Report(r => r.ShowAddSchool = true), Links).Html, StringComparison.Ordinal);
            Assert.DoesNotContain("Add your school in your profile", WeeklyReportEmailBuilder.Report(Report(), Links).Html, StringComparison.Ordinal);
        }

        [Fact]
        public void ContributionsAreCountedByKindAndOutcome()
        {
            var html = WeeklyReportEmailBuilder.Report(Report(r => r.Contributions =
            [
                new() { Category = CategoryType.SchoolImage, Status = Status.Confirmed, Count = 2 },
                new() { Category = CategoryType.SchoolComment, Status = Status.Review, Count = 1 },
                new() { Category = CategoryType.School, Status = Status.Rejected, Count = 1 },
            ]), Links).Html;

            Assert.Contains("2 school photos", html, StringComparison.Ordinal);
            Assert.Contains("Approved", html, StringComparison.Ordinal);
            Assert.Contains("1 school review", html, StringComparison.Ordinal);
            Assert.Contains("Waiting for a check", html, StringComparison.Ordinal);
            Assert.Contains("Not accepted", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ReminderNamesWhatIsLeft()
        {
            var email = WeeklyReportEmailBuilder.Reminder(Report(r => r.Plan = new()
            {
                Title = "Pro",
                PeriodEnd = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero),
                Quotas = [new() { Name = "Past paper downloads", Used = 12, Limit = 30 }, new() { Name = "Exam downloads", Used = 5, Limit = 20 }],
            }), Links);

            Assert.Equal(WeeklyReportKind.Reminder, email.Kind);
            Assert.Equal("Your GamaTrain plan: 18 of 30 left for Past paper downloads", email.Subject);
            Assert.Contains("You still have 18 of 30 left for Past paper downloads before 24 Nov.", email.Html, StringComparison.Ordinal);
            Assert.Contains("15 of 20 left", email.Html, StringComparison.Ordinal);
            Assert.Contains("at most once every 4 weeks", email.Html, StringComparison.Ordinal);
            Assert.Contains("type=weekly-report", email.Html, StringComparison.Ordinal);
        }

        private static WeeklyReportPlanDto Plan(int used, int? limit) => new() { Title = "Pro", Quotas = [new() { Name = "Past paper downloads", Used = used, Limit = limit }] };

        private static WeeklyReportDto Report(Action<WeeklyReportDto>? change = null)
        {
            var report = new WeeklyReportDto { UserId = 7, WeekStart = Monday, WeekEnd = Monday.AddDays(7) };
            change?.Invoke(report);
            return report;
        }
    }
}
