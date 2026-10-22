namespace GamaEdtech.Data.Dto.Dashboard
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Admin dashboard date range rules (2026-10-22), the one place they live: a missing range means the last 30 days,
    /// ranges are whole UTC days, at most 366 days long, and the daily trend buckets cover every day in the range.
    /// </summary>
    public static class DashboardRange
    {
        public const int DefaultDays = 30;
        public const int MaxDays = 366;

        /// <summary>The range as [from 00:00 UTC, to+1 00:00 UTC). Swaps reversed dates and clamps the length to MaxDays (keeping the end).</summary>
        public static (DateTimeOffset From, DateTimeOffset ToExclusive) Resolve(DateOnly? startDate, DateOnly? endDate, DateOnly today)
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

            return (new DateTimeOffset(start, TimeOnly.MinValue, TimeSpan.Zero), new DateTimeOffset(end.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero));
        }

        /// <summary>Every UTC day in the range, oldest first.</summary>
        public static IReadOnlyList<DateOnly> Days(DateTimeOffset from, DateTimeOffset toExclusive)
        {
            var days = new List<DateOnly>();
            for (var day = DateOnly.FromDateTime(from.UtcDateTime); day < DateOnly.FromDateTime(toExclusive.UtcDateTime); day = day.AddDays(1))
            {
                days.Add(day);
            }

            return days;
        }

        /// <summary>Lower-case hex SHA-256 of an IP address (trimmed, lower-case), or null for none. Used by the self-invite check.</summary>
        public static string? HashIp(string? ipAddress)
        {
            var ip = ipAddress?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(ip) ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
        }
    }
}
