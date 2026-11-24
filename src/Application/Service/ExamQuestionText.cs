namespace GamaEdtech.Application.Service
{
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>gama-api's question text rules (<c>Examtest_lib</c>), shared by the native exam reads.</summary>
    internal static partial class ExamQuestionText
    {
        /// <summary>gama-api's <c>typeIdToTitle</c>: a question type's name; an unknown code is sent as its number.</summary>
        internal static string? TypeTitle(byte? type) => type switch
        {
            null => null,
            1 => "fourchoice",
            2 => "descriptive",
            4 => "twochoice",
            8 => "tf",
            16 => "blank",
            32 => "shortanswer",
            _ => type.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        /// <summary>gama-api's <c>questionTitleSeparator</c>: a question may carry its title as <c>@@title@@</c> (usually its own
        /// paragraph); the title is taken out of the question.</summary>
        internal static (string Title, string? Question) SplitTitle(string? question)
        {
            if (string.IsNullOrEmpty(question))
            {
                return (string.Empty, question);
            }

            var match = TitleRegex().Match(question);
            if (!match.Success)
            {
                return (string.Empty, question);
            }

            var title = match.Groups[1].Value;
            return (title, question.Replace($"<p>@@{title}@@</p>", string.Empty, StringComparison.Ordinal).Replace($"@@{title}@@", string.Empty, StringComparison.Ordinal));
        }

        /// <summary>The question's text without tags or math delimiters, for its page title and description.</summary>
        internal static string PlainText(string? html) =>
            SpacesRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace((html ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal), " ")), " ").Trim();

        /// <summary>The question's text without tags but with its TeX kept (<c>$...$</c>, <c>\(...\)</c>), cut to about
        /// <paramref name="length"/> characters, for a list the admin panel typesets with MathJax. A cut never leaves a formula
        /// open: it moves back to before that formula, since an unclosed one would show as raw TeX.</summary>
        internal static string Excerpt(string? html, int length)
        {
            var text = SpacesRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(html ?? string.Empty, " ")), " ").Trim();
            if (text.Length <= length)
            {
                return text;
            }

            var cut = text[..length];
            var dollars = DollarRegex().Matches(cut);
            if (dollars.Count % 2 == 1)
            {
                cut = cut[..dollars[^1].Index];
            }

            var open = cut.LastIndexOf(@"\(", StringComparison.Ordinal);
            if (open >= 0 && cut.LastIndexOf(@"\)", StringComparison.Ordinal) < open)
            {
                cut = cut[..open];
            }

            return cut.TrimEnd() + "…";
        }

        [GeneratedRegex(@"(?<!\\)\$")]
        private static partial Regex DollarRegex();

        [GeneratedRegex("@@(.*?)@@", RegexOptions.Singleline)]
        private static partial Regex TitleRegex();

        [GeneratedRegex("<[^>]*>")]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"[\s ]+")]
        private static partial Regex SpacesRegex();
    }
}
