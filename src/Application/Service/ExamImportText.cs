namespace GamaEdtech.Application.Service
{
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Turns the light markup the AI writes in an imported question into the HTML subset gama-api keeps for questions
    /// (examTests keeps only <c>p span b br u</c>; anything else is stripped there). Math stays TeX for MathJax, which
    /// gamatrain renders with <c>$...$</c>, <c>\(...\)</c>, <c>$$...$$</c> and <c>\[...\]</c>.
    /// Markup: a blank line starts a paragraph, a single newline is a line break, <c>**bold**</c>, <c>__underline__</c>
    /// (a run of underscores with nothing inside, a fill-in blank, stays literal). Bold and underline apply outside math
    /// only, so TeX like <c>a_{i}</c> or <c>x__1</c> is never touched.
    /// </summary>
    internal static partial class ExamImportText
    {
        /// <summary>Plain text with markup to <c>&lt;p&gt;..&lt;/p&gt;</c> paragraphs; empty input gives an empty string.</summary>
        public static string ToHtml(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
            StringBuilder html = new();
            foreach (var paragraph in ParagraphBreakRegex().Split(normalized).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                _ = html.Append("<p>").Append(Paragraph(paragraph.Trim())).Append("</p>");
            }

            return html.ToString();
        }

        /// <summary>The visible length, roughly what gama-api measures: tags stripped, entities decoded.</summary>
        public static int PlainLength(string? html) => string.IsNullOrEmpty(html) ? 0 : WebUtility.HtmlDecode(TagRegex().Replace(html, string.Empty)).Trim().Length;

        private static string Paragraph(string text)
        {
            // Split keeps the captured math segments at the odd indexes.
            var parts = MathRegex().Split(text);
            StringBuilder output = new();
            for (var i = 0; i < parts.Length; i++)
            {
                _ = output.Append(i % 2 == 1 ? Escape(parts[i]) : Inline(parts[i]));
            }

            return string.Join("<br>", output.ToString().Split('\n').Select(t => t.Trim()));
        }

        private static string Inline(string segment)
        {
            var html = AllowedLiteralRegex().Replace(Escape(segment), t => $"<{t.Groups[1].Value}{t.Groups[2].Value.ToLowerInvariant()}>");
            html = BoldRegex().Replace(html, "<b>$1</b>");
            return UnderlineRegex().Replace(html, "<u>$1</u>");
        }

        private static string Escape(string value) => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        [GeneratedRegex(@"(\$\$.+?\$\$|\\\[.+?\\\]|\\\(.+?\\\)|(?<!\\)\$.+?(?<!\\)\$)", RegexOptions.Singleline)]
        private static partial Regex MathRegex();

        [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Singleline)]
        private static partial Regex BoldRegex();

        [GeneratedRegex(@"(?<!_)__(?=[^\s_])(.+?)(?<=[^\s_])__(?!_)", RegexOptions.Singleline)]
        private static partial Regex UnderlineRegex();

        /// <summary>Tags the AI may already have written literally; they survive escaping.</summary>
        [GeneratedRegex(@"&lt;(/?)(b|u|br)\s*/?&gt;", RegexOptions.IgnoreCase)]
        private static partial Regex AllowedLiteralRegex();

        [GeneratedRegex(@"\n\s*\n")]
        private static partial Regex ParagraphBreakRegex();

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex TagRegex();
    }
}
