namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Net;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    /// <summary>
    /// gama-api's output rules for a past paper, reproduced exactly (its English site's <c>url_title</c>,
    /// <c>formatSize</c> and <c>getMonthTitle</c>; dates are UTC only, gama-api's Jalali copies are not sent), so the native paper page
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

        /// <summary>
        /// gama-api's <c>topicsDecoder</c> season test, kept as it behaves: <c>!empty($meta['season']) &amp;&amp;
        /// in_array('season', $meta)</c>. PHP 8's loose <c>in_array</c> finds 'season' only for a value that is <c>true</c> or the
        /// string "season", so a topic marked <c>"season":1</c> (the usual form) is not a season by its metadata; only a
        /// title starting with <c>**</c> makes it one then.
        /// </summary>
        public static bool IsSeason(string? metadata)
        {
            JsonObject? meta;
            try
            {
                meta = string.IsNullOrWhiteSpace(metadata) ? null : JsonNode.Parse(metadata) as JsonObject;
            }
            catch (JsonException)
            {
                return false;
            }

            return meta is not null && !PhpEmpty(meta["season"]) && meta.Any(t => LooselyEqualsSeason(t.Value));

            static bool LooselyEqualsSeason(JsonNode? value) => value is JsonValue v
                && ((v.TryGetValue<bool>(out var flag) && flag) || (v.TryGetValue<string>(out var text) && text == "season"));
        }

        // PHP's empty(): null, false, 0, 0.0, "", "0" and an empty array.
        private static bool PhpEmpty(JsonNode? value) => value switch
        {
            null => true,
            JsonArray array => array.Count == 0,
            JsonObject => false,
            JsonValue v when v.TryGetValue<bool>(out var flag) => !flag,
            JsonValue v when v.TryGetValue<string>(out var text) => text is "" or "0",
            JsonValue v when v.TryGetValue<decimal>(out var number) => number == 0,
            _ => false,
        };

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
