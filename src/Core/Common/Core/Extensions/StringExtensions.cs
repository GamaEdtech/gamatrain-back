namespace GamaEdtech.Common.Core.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using System.Text.RegularExpressions;

    public static partial class StringExtensions
    {
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)] this string? str) => string.IsNullOrEmpty(str);

        public static string? Left(this string? str, int len) => str?.Length < len ? throw new ArgumentException("len argument can not be bigger than given string's length!") : (str?[..len]);

        public static string? Right([NotNull] this string str, int len) => str.Length < len
                ? throw new ArgumentException("len argument can not be bigger than given string's length!")
                : str.Substring(str.Length - len, len);

        public static string[]? Split(this string? str, string separator) => str?.Split([separator], StringSplitOptions.None);

        public static string[]? Split(this string? str, string separator, StringSplitOptions options) => str?.Split([separator], options);

        public static IEnumerable<ReadOnlyMemory<char>> Split(this ReadOnlyMemory<char> chars, char separator, StringSplitOptions options = StringSplitOptions.None)
        {
            int index;
            while ((index = chars.Span.IndexOf(separator)) >= 0)
            {
                var slice = chars[..index];
                if ((options & StringSplitOptions.TrimEntries) == StringSplitOptions.TrimEntries)
                {
                    slice = slice.Trim();
                }

                if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || slice.Length > 0)
                {
                    yield return slice;
                }

                chars = chars[(index + 1)..];
            }

            if ((options & StringSplitOptions.TrimEntries) == StringSplitOptions.TrimEntries)
            {
                chars = chars.Trim();
            }

            if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || chars.Length > 0)
            {
                yield return chars;
            }
        }

        public static string[]? SplitToLines(this string? str) => str?.Split(Environment.NewLine);

        public static string[]? SplitToLines(this string? str, StringSplitOptions options) => str?.Split(Environment.NewLine, options);

        public static T? ToEnum<T>(this string? value)
            where T : struct => string.IsNullOrEmpty(value) ? null : Enum.Parse<T>(value);

        public static T? ToEnum<T>(this string? value, bool ignoreCase)
            where T : struct => string.IsNullOrEmpty(value) ? null : Enum.Parse<T>(value, ignoreCase);

        public static byte[] GetBytes(this string str) => str.GetBytes(Encoding.UTF8);

        public static byte[] GetBytes([NotNull] this string str, [NotNull] Encoding encoding) => encoding.GetBytes(str);

        public static bool HasValue(this string value, bool ignoreWhiteSpace = true)
            => ignoreWhiteSpace ? !string.IsNullOrWhiteSpace(value) : !string.IsNullOrEmpty(value);

        public static bool HasValues(bool ignoreWhiteSpace, params string[] values) =>
            ignoreWhiteSpace ? values.All(a => !string.IsNullOrWhiteSpace(a))
                : values.All(a => !string.IsNullOrEmpty(a));

        public static string TrimEnd([NotNull] this string source, [NotNull] string value)
        {
            while (source.EndsWith(value, StringComparison.OrdinalIgnoreCase))
            {
                source = source[..^value.Length];
            }

            return source;
        }

        public static int ToInt(this string value) => Convert.ToInt32(value);
        public static decimal ToDecimal(this string value) => Convert.ToDecimal(value);
        public static string ToNumeric(this long value) => value.ToString("N0");

        public static string ToNumeric(this int value) => value.ToString("N0");

        public static string ToNumeric(this decimal value) => value.ToString("N0");

        public static string ToNumeric(this double value) => value.ToString("N0");

        public static string ToCamelCase(this string s)
        {
            if (string.IsNullOrEmpty(s) || !char.IsUpper(s[0]))
            {
                return s;
            }

            var array = s.ToCharArray();
            for (var i = 0; i < array.Length && (i != 1 || char.IsUpper(array[i])); i++)
            {
                var flag = i + 1 < array.Length;
                if (i > 0 && flag && !char.IsUpper(array[i + 1]))
                {
                    break;
                }

                array[i] = char.ToLowerInvariant(array[i]);
            }

            return new string(array);
        }

        public static string ToCurrency(this int value) =>
            //fa-IR => current culture currency symbol => ریال
            //123456 => "123,123ریال"
            value.ToString("C0");

        public static string ToCurrency(this decimal value) => value.ToString("C0");

        private static readonly Dictionary<char, char> EnToFaDigits = new()
        {
            { '0', '۰' }, { '1', '۱' }, { '2', '۲' }, { '3', '۳' }, { '4', '۴' },
            { '5', '۵' }, { '6', '۶' }, { '7', '۷' }, { '8', '۸' }, { '9', '۹' }
        };

        private static readonly Dictionary<char, char> FaToEnDigits = new()
        {
            { '۰', '0' }, { '۱', '1' }, { '۲', '2' }, { '۳', '3' }, { '۴', '4' },
            { '۵', '5' }, { '۶', '6' }, { '۷', '7' }, { '۸', '8' }, { '۹', '9' },
            { '٠', '0' }, { '١', '1' }, { '٢', '2' }, { '٣', '3' }, { '٤', '4' },
            { '٥', '5' }, { '٦', '6' }, { '٧', '7' }, { '٨', '8' }, { '٩', '9' }
        };

        private static readonly Dictionary<char, char> PersianCharFixes = new()
        {
            { 'ﮎ', 'ک' }, { 'ﮏ', 'ک' }, { 'ﮐ', 'ک' }, { 'ﮑ', 'ک' }, { 'ك', 'ک' },
            { 'ي', 'ی' }, { 'ھ', 'ه' }
        };

        private static readonly Dictionary<string, string> PersianStringFixes = new()
        {
            { "\u00A0", " " },
            { "\u200C", " " }
        };

        public static string En2Fa(this string str)
            => string.IsNullOrEmpty(str) ? str :
            string.Concat(str.Select(ch => EnToFaDigits.TryGetValue(ch, out var mapped) ? mapped : ch));

        public static string Fa2En(this string str) =>
            string.IsNullOrEmpty(str) ? str :
            string.Concat(str.Select(ch => FaToEnDigits.TryGetValue(ch, out var mapped)
            ? mapped : ch));

        public static string FixPersianChars(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            var fixedChars = string.Concat(str.Select(ch => PersianCharFixes.TryGetValue(ch, out var mapped) ? mapped : ch));

            foreach (var (key, value) in PersianStringFixes)
            {
                fixedChars = fixedChars.Replace(key, value, StringComparison.Ordinal);
            }

            return fixedChars;
        }

        public static Guid ToGuid(this string str)
        {
            try
            {
                return Guid.Parse(str);
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

        public static string? CleanString([NotNull] this string str) => str.Trim().FixPersianChars().Fa2En().NullIfEmpty();

        public static string? NullIfEmpty(this string str) => str?.Length == 0 ? null : str;

        public static string? Fix(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = MyRegex().Replace(text.Trim(), " ");

            return string.IsNullOrEmpty(text) ? null : text;
        }

        public static string? ConvertToPersian(this string searchTerm) => searchTerm?
                .ToCharArray()
                .Aggregate("", (current, charItem) => current + charItem switch
                {
                    'Q' => 'ض',
                    'q' => 'ض',
                    'W' => 'ص',
                    'w' => 'ص',
                    'E' => 'ث',
                    'e' => 'ث',
                    'R' => 'ق',
                    'r' => 'ق',
                    'T' => 'ف',
                    't' => 'ف',
                    'Y' => 'غ',
                    'y' => 'غ',
                    'U' => 'ع',
                    'u' => 'ع',
                    'I' => 'ه',
                    'i' => 'ه',
                    'O' => 'خ',
                    'o' => 'خ',
                    'P' => 'ح',
                    'p' => 'ح',
                    '{' => 'ج',
                    '[' => 'ج',
                    '}' => 'چ',
                    ']' => 'چ',
                    'A' => 'ش',
                    'a' => 'ش',
                    'S' => 'س',
                    's' => 'س',
                    'D' => 'ی',
                    'd' => 'ی',
                    'F' => 'ب',
                    'f' => 'ب',
                    'G' => 'ل',
                    'g' => 'ل',
                    'H' => 'ا',
                    'h' => 'ا',
                    'J' => 'ت',
                    'j' => 'ت',
                    'K' => 'ن',
                    'k' => 'ن',
                    'L' => 'م',
                    'l' => 'م',
                    ';' => 'ک',
                    ':' => 'ک',
                    '\'' => 'گ',
                    '\"' => 'گ',
                    'Z' => 'ظ',
                    'z' => 'ظ',
                    'X' => 'ط',
                    'x' => 'ط',
                    'C' => 'ز',
                    'c' => 'ز',
                    'V' => 'ر',
                    'v' => 'ر',
                    'B' => 'ذ',
                    'b' => 'ذ',
                    'N' => 'د',
                    'n' => 'د',
                    'M' => 'پ',
                    'm' => 'پ',
                    '<' => 'و',
                    '\\' => 'پ',
                    'ي' => 'ی',
                    'ك' => 'ک',
                    _ => charItem
                });
        public static string SqlReplacement(this string term)
            => string.IsNullOrEmpty(term) ? "%%" : $"%{term.Replace("ا", "[ا|آ]", StringComparison.Ordinal)}%";

        public static string DecodeAmiFormatPhoneNumber(this string callerIdNum)
        {
            var tenLastChars = callerIdNum?.AsEnumerable().TakeLast(10).ToArray();
            return tenLastChars?[0] == '9' && tenLastChars.Length == 10
                ? $"0{new string(tenLastChars)}"
                : (tenLastChars?[0]) == '9' || tenLastChars?.Length >= 4 ? $"0{new string(tenLastChars)}"
                : new string(tenLastChars);
        }

        public static bool IsNumber(this string stringValue)
        {
            var pattern = @"^-?[0-9]+(?:\.[0-9]+)?$";
            var regex = new Regex(pattern);
            return regex.IsMatch(stringValue);
        }

        public static DateTime ToDateTime(this string value)
         => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex MyRegex();
    }
}
