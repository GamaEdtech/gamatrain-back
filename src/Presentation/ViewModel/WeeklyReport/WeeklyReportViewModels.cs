namespace GamaEdtech.Presentation.ViewModel.WeeklyReport
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    /// <summary><c>GET weeklyreports/settings</c>: the caller's own weekly report setting.</summary>
    public sealed class WeeklyReportSettingsResponseViewModel
    {
        /// <summary>False once they turned it off (settings page or the email's link).</summary>
        public bool Subscribed { get; set; }

        /// <summary>Whether their current plan includes the weekly report; without it nothing is sent either way.</summary>
        public bool IncludedInPlan { get; set; }

        public string? Email { get; set; }

        /// <summary>Reports go only to a confirmed address.</summary>
        public bool EmailConfirmed { get; set; }

        public DateTimeOffset? LastSentDate { get; set; }
    }

    /// <summary><c>GET admin/weeklyreports/status</c>: reports go out only when both allow it.</summary>
    public sealed class WeeklyReportStatusResponseViewModel
    {
        /// <summary>Whether this server may send it at all (<c>WeeklyReport:Enabled</c>, only production).</summary>
        public bool EnabledOnServer { get; set; }

        /// <summary>Paused for everyone by an admin.</summary>
        public bool Paused { get; set; }
    }

    /// <summary><c>GET admin/weeklyreports/preview</c>.</summary>
    public sealed class WeeklyReportPreviewRequestViewModel
    {
        public long UserId { get; set; }

        /// <summary>Any day of the week to preview (yyyy-MM-dd, UTC); empty for last week.</summary>
        public DateOnly? WeekOf { get; set; }
    }

    /// <summary>The email one user would get for the week, exactly as it would be sent.</summary>
    public sealed class WeeklyReportPreviewResponseViewModel
    {
        public bool HasActivity { get; set; }

        public bool WouldSend { get; set; }

        /// <summary>Why the job wouldn't send it, when it wouldn't.</summary>
        public string? NotSentReason { get; set; }

        [JsonConverter(typeof(EnumerationConverter<WeeklyReportKind, byte>))]
        public WeeklyReportKind? Kind { get; set; }

        public string? Subject { get; set; }

        /// <summary>The email's full HTML document.</summary>
        public string? Html { get; set; }
    }
}
