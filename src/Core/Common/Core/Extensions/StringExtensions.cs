namespace GamaEdtech.Common.Core.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

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
    }
}
