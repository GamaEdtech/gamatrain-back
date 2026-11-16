namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>
    /// gama-api's output rules for a past paper, reproduced exactly (its English site's <c>url_title</c>,
    /// <c>formatSize</c>, <c>getMonthTitle</c>, Jalali dates and CodeIgniter's <c>timespan</c>), so the native paper page
    /// and lists answer what gama-api answers. Pure.
    /// </summary>
    public static partial class PastPaperOutput
    {
        /// <summary>gama-api's <c>url_title(textSanitize(title))</c>: entities and anything but ASCII letters, digits, space,
        /// <c>_</c> and <c>-</c> removed, spaces turned into one <c>-</c>, trimmed.</summary>
        public static string TitleUrl(string? title)
        {
            // textSanitize (htmlspecialchars) turns & < > " ' into entities; the first rule then removes every entity.
            var text = WebUtility.HtmlEncode(title ?? string.Empty).Replace("&#39;", "&#039;", StringComparison.Ordinal);
            text = EntityRegex().Replace(text, string.Empty);
            text = NotWordRegex().Replace(text, string.Empty);
            text = SpaceRegex().Replace(text, "-");
            text = DashesRegex().Replace(text, "-");
            return text.Trim().Trim('-');
        }

        /// <summary>gama-api's <c>formatSize</c>: bytes, KB, MB... (powers of 1024), rounded half away from zero.</summary>
        public static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)} {units[unit]}";
        }

        /// <summary>gama-api's English <c>getMonthTitle</c>: the short month name (Jan..Dec); null for no month.</summary>
        public static string? MonthTitle(int? month) =>
            month is >= 1 and <= 12 ? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Value) : null;

        /// <summary>The Jalali (Persian calendar) date of a stored date as gama-api writes it: <c>yyyy/MM/dd</c> of the date as
        /// written (gama-api reads and formats it in the same zone, so the calendar day never moves).</summary>
        public static string Jalali(DateTime written)
        {
            PersianCalendar calendar = new();
            return string.Create(CultureInfo.InvariantCulture,
                $"{calendar.GetYear(written):0000}/{calendar.GetMonth(written):00}/{calendar.GetDayOfMonth(written):00}");
        }

        /// <summary>
        /// A list item's date (<c>date_human_friendly($date, 1, 'g_to_j')</c>): the Jalali date when it is more than a day
        /// old, otherwise how long ago in its largest unit, CodeIgniter's English <c>timespan</c> followed by " قبل".
        /// </summary>
        public static string ListDate(DateTime written, DateTime now)
        {
            var age = now - written;
            return age > TimeSpan.FromDays(1) ? Jalali(written) : $"{Ago(age)} قبل";
        }

        /// <summary>CodeIgniter's English <c>timespan($time, '', 1)</c>: the largest whole unit, "1 Hour", "5 Minutes".</summary>
        internal static string Ago(TimeSpan age)
        {
            var seconds = (long)Math.Max(age.TotalSeconds, 0);
            (long Size, string One, string Many)[] units =
            [
                (31_557_600, "Year", "Years"),
                (2_629_743, "Month", "Months"),
                (604_800, "Week", "Weeks"),
                (86_400, "Day", "Days"),
                (3_600, "Hour", "Hours"),
                (60, "Minute", "Minutes"),
            ];
            foreach (var (size, one, many) in units)
            {
                if (seconds >= size)
                {
                    var count = seconds / size;
                    return string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
                }
            }

            return string.Create(CultureInfo.InvariantCulture, $"{seconds} {(seconds == 1 ? "Second" : "Seconds")}");
        }

        /// <summary>A stored date as gama-api's MySQL sends it: <c>yyyy-MM-dd HH:mm:ss</c>, in its zone (UTC).</summary>
        public static string Written(DateTimeOffset date) => date.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>A number as MySQL sends a numeric column: invariant digits, no trailing zeros (<c>5</c>, <c>1.5</c>).</summary>
        public static string Number(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

        /// <summary>A whole number as MySQL sends it.</summary>
        public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        [GeneratedRegex("&.+?;", RegexOptions.IgnoreCase)]
        private static partial Regex EntityRegex();

        [GeneratedRegex("[^A-Za-z0-9_ _-]")]
        private static partial Regex NotWordRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex SpaceRegex();

        [GeneratedRegex("-+")]
        private static partial Regex DashesRegex();
    }
}
