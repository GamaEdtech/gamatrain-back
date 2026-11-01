namespace GamaEdtech.Data.Dto.Dashboard
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Admin dashboard date range rules (2026-10-22), the one place they live: a missing range means the last 30 days,
    /// ranges are whole days in the viewer's time zone (UTC when none is given), at most 366 days long, and the daily
    /// trend buckets cover every day in the range.
    /// </summary>
    public static class DashboardRange
    {
        public const int DefaultDays = 30;
        public const int MaxDays = 366;

        /// <summary>
        /// The range as [from 00:00, to+1 00:00) in <paramref name="timeZone"/> (UTC when null), as instants. Swaps
        /// reversed dates and clamps the length to MaxDays (keeping the end).
        /// </summary>
        public static (DateTimeOffset From, DateTimeOffset ToExclusive) Resolve(DateOnly? startDate, DateOnly? endDate, DateOnly today, TimeZoneInfo? timeZone = null)
        {
            var end = endDate ?? today;
            var start = startDate ?? end.AddDays(-(DefaultDays - 1));
            if (start > end)
            {
                (start, end) = (end, start);
            }

            if (end.DayNumber - start.DayNumber + 1 > MaxDays)
            {
                start = end.AddDays(-(MaxDays - 1));
            }

            var zone = timeZone ?? TimeZoneInfo.Utc;
            return (StartOfDay(start, zone), StartOfDay(end.AddDays(1), zone));
        }

        /// <summary>Every day in the range in <paramref name="timeZone"/> (UTC when null), oldest first.</summary>
        public static IReadOnlyList<DateOnly> Days(DateTimeOffset from, DateTimeOffset toExclusive, TimeZoneInfo? timeZone = null)
        {
            var zone = timeZone ?? TimeZoneInfo.Utc;
            var days = new List<DateOnly>();
            for (var day = LocalDay(from, zone); day < LocalDay(toExclusive, zone); day = day.AddDays(1))
            {
                days.Add(day);
            }

            return days;
        }

        /// <summary>The calendar day an instant falls on in <paramref name="timeZone"/>.</summary>
        public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo timeZone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

        /// <summary>The instant a day starts in <paramref name="timeZone"/>.</summary>
        private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo timeZone)
        {
            var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(midnight, timeZone.GetUtcOffset(midnight));
        }

        /// <summary>Lower-case hex SHA-256 of an IP address (trimmed, lower-case), or null for none. Used by the self-invite check.</summary>
        public static string? HashIp(string? ipAddress)
        {
            var ip = ipAddress?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(ip) ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
        }
    }
}
