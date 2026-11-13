namespace GamaEdtech.Data.Dto.WeeklyReport
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Everything one user's weekly report shows, for one UTC week (Monday to Sunday). Built only from data this backend
    /// stores; exam scores live on gama-api and are not in it. See docs/business/notifications.md, "Weekly report".
    /// </summary>
    public sealed class WeeklyReportDto
    {
        public long UserId { get; set; }

        public string? FirstName { get; set; }

        /// <summary>Monday 00:00 UTC.</summary>
        public DateTimeOffset WeekStart { get; set; }

        /// <summary>The next Monday 00:00 UTC (exclusive).</summary>
        public DateTimeOffset WeekEnd { get; set; }

        public bool IsTeacher { get; set; }

        /// <summary>Practice questions answered this week (<c>TestSubmission</c>).</summary>
        public int QuestionsAnswered { get; set; }

        public int QuestionsCorrect { get; set; }

        /// <summary>Practice streak at the end of the week, in days.</summary>
        public int Streak { get; set; }

        /// <summary>Points earned this week (credits in the points ledger).</summary>
        public long PointsEarned { get; set; }

        public WeeklyReportPlanDto? Plan { get; set; }

        /// <summary>What the plan was used for this week, refunds netted out.</summary>
        public IReadOnlyList<WeeklyReportCountDto> UsageThisWeek { get; set; } = [];

        /// <summary>The user's contributions submitted this week, by kind and outcome.</summary>
        public IReadOnlyList<WeeklyReportContributionDto> Contributions { get; set; } = [];

        public WeeklyReportSchoolDto? School { get; set; }

        /// <summary>The "add your school" line is shown in the first few reports only.</summary>
        public bool ShowAddSchool { get; set; }

        public WeeklyReportTeacherDto? Teacher { get; set; }
    }

    public sealed class WeeklyReportPlanDto
    {
        public string? Title { get; set; }

        /// <summary>When the current period ends.</summary>
        public DateTimeOffset? PeriodEnd { get; set; }

        /// <summary>True when it renews at <see cref="PeriodEnd"/>, false when it ends then.</summary>
        public bool Renews { get; set; }

        public IReadOnlyList<WeeklyReportQuotaDto> Quotas { get; set; } = [];
    }

    /// <summary>One quota bucket of the plan (it can cover several features).</summary>
    public sealed class WeeklyReportQuotaDto
    {
        public required string Name { get; set; }

        public int Used { get; set; }

        /// <summary>Null = unlimited.</summary>
        public int? Limit { get; set; }
    }

    public sealed class WeeklyReportCountDto
    {
        public required string Name { get; set; }

        public int Count { get; set; }
    }

    public sealed class WeeklyReportContributionDto
    {
        public required CategoryType Category { get; set; }

        public required Status Status { get; set; }

        public int Count { get; set; }
    }

    /// <summary>The user's school, compared on the public rating only (never the internal ranking).</summary>
    public sealed class WeeklyReportSchoolDto
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        /// <summary>Average approved review rating, 0-5; null with no reviews.</summary>
        public double? Rating { get; set; }

        public int Reviews { get; set; }

        public string? CityName { get; set; }

        /// <summary>Average rating of the city's rated schools; null when fewer than 3 are rated.</summary>
        public double? CityAverage { get; set; }

        public int NewReviews { get; set; }

        public int NewPhotos { get; set; }

        public int NewMembers { get; set; }
    }

    /// <summary>The teacher part. Earnings are never put in the email: it links to the dashboard instead.</summary>
    public sealed class WeeklyReportTeacherDto
    {
        /// <summary>Downloads of the teacher's past papers this week (download commissions).</summary>
        public int PaperDownloads { get; set; }

        /// <summary>People who signed up with the teacher's invite link this week.</summary>
        public int JoinedByInvite { get; set; }

        public int PostsPublished { get; set; }
    }

    /// <summary>One email as it would be sent: what the admin preview returns, and what the job sends.</summary>
    public sealed class WeeklyReportEmailDto
    {
        public required WeeklyReportKind Kind { get; set; }

        public required string Subject { get; set; }

        public required string Html { get; set; }
    }

    /// <summary>The admin preview of one user's email for one week.</summary>
    public sealed class WeeklyReportPreviewDto
    {
        public bool HasActivity { get; set; }

        /// <summary>Whether the job would send anything to this user for this week, and why not when it wouldn't.</summary>
        public bool WouldSend { get; set; }

        public string? NotSentReason { get; set; }

        public WeeklyReportEmailDto? Email { get; set; }
    }

    /// <summary>The user's own weekly report setting, for the settings page.</summary>
    public sealed class WeeklyReportSettingsDto
    {
        public bool Subscribed { get; set; }

        /// <summary>Whether their current plan includes the weekly report.</summary>
        public bool IncludedInPlan { get; set; }

        public string? Email { get; set; }

        public bool EmailConfirmed { get; set; }

        public DateTimeOffset? LastSentDate { get; set; }
    }

    /// <summary>What one run of the weekly job did.</summary>
    public sealed class WeeklyReportRunDto
    {
        public DateTimeOffset WeekStart { get; set; }

        public int Candidates { get; set; }

        public int Reports { get; set; }

        public int Reminders { get; set; }

        public int Skipped { get; set; }

        public int Failed { get; set; }
    }
}
