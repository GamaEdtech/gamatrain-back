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
    /// only, so TeX like <c>a_{i}</c> or <c>x__1</c> is never touched. A power written with a caret outside math
    /// (<c>m s^-1</c>, <c>10^12</c>, <c>x^(2)</c>, <c>s^{-1}</c>), which the AI sometimes does despite the guide, becomes
    /// inline TeX (<c>m s\(^{-1}\)</c>): gama-api strips <c>sup</c>, and a bare caret was shown and exported as typed.
    /// A dollar sign for money (<c>$900</c>) is escaped to <c>\$</c>, which MathJax shows as a plain "$": two amounts in
    /// one paragraph were read as one formula from the first "$" to the next (exam 2528). Inside math, the LaTeX-only
    /// <c>\degree</c> and <c>\textdegree</c>, which MathJax does not know, become <c>^{\circ}</c> (exam 2527).
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
            var parts = MathRegex().Split(EscapeMoney(text));
            StringBuilder output = new();
            for (var i = 0; i < parts.Length; i++)
            {
                _ = output.Append(i % 2 == 1 ? Escape(MathCommand(parts[i])) : Inline(parts[i]));
            }

            return string.Join("<br>", output.ToString().Split('\n').Select(t => t.Trim()));
        }

        private static string Inline(string segment)
        {
            var html = AllowedLiteralRegex().Replace(Escape(segment), t => $"<{t.Groups[1].Value}{t.Groups[2].Value.ToLowerInvariant()}>");
            html = CaretPowerRegex().Replace(html, t => $"\\(^{{{t.Groups["power"].Value.Replace('\u2212', '-')}}}\\)");
            html = BoldRegex().Replace(html, "<b>$1</b>");
            return UnderlineRegex().Replace(html, "<u>$1</u>");
        }

        /// <summary>
        /// Escapes each "$" that is money rather than the start of a formula, reading left to right the way MathJax
        /// pairs delimiters. A "$" directly before a digit is money when no "$" follows it, or when the next "$" comes
        /// after a space and before a digit: the gap between two amounts ("$900. This was recorded for $90"), never a
        /// formula's end, which follows its last symbol ("$2 \times 3$"). Formulas are skipped whole.
        /// </summary>
        internal static string EscapeMoney(string text)
        {
            if (!text.Contains('$', StringComparison.Ordinal))
            {
                return text;
            }

            StringBuilder output = new(text.Length + 8);
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    // An escaped "\$" and a \( \) or \[ \] formula are copied as they are.
                    var close = text[i + 1] switch
                    {
                        '(' => text.IndexOf(@"\)", i + 2, StringComparison.Ordinal),
                        '[' => text.IndexOf(@"\]", i + 2, StringComparison.Ordinal),
                        _ => -1,
                    };
                    var end = close < 0 ? i + 2 : close + 2;
                    _ = output.Append(text, i, end - i);
                    i = end;
                    continue;
                }

                if (c != '$')
                {
                    _ = output.Append(c);
                    i++;
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '$')
                {
                    var close = text.IndexOf("$$", i + 2, StringComparison.Ordinal);
                    var end = close < 0 ? text.Length : close + 2;
                    _ = output.Append(text, i, end - i);
                    i = end;
                    continue;
                }

                var next = NextDollar(text, i + 1);
                if (i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])
                    && (next < 0 || (char.IsWhiteSpace(text[next - 1]) && next + 1 < text.Length && char.IsAsciiDigit(text[next + 1]))))
                {
                    _ = output.Append(@"\$");
                    i++;
                    continue;
                }

                // A formula: copy it whole, so a "$" inside it is never taken for money.
                var formulaEnd = next < 0 ? text.Length : next + 1;
                _ = output.Append(text, i, formulaEnd - i);
                i = formulaEnd;
            }

            return output.ToString();
        }

        /// <summary>The next unescaped single "$" from <paramref name="start"/>, or -1.</summary>
        private static int NextDollar(string text, int start)
        {
            var i = start;
            while (i < text.Length)
            {
                if (text[i] == '$')
                {
                    return i;
                }

                // Skip the character after a backslash, so an escaped "\$" is not a delimiter.
                i += text[i] == '\\' ? 2 : 1;
            }

            return -1;
        }

        /// <summary>LaTeX commands MathJax does not know, rewritten to ones it draws the same way.</summary>
        private static string MathCommand(string math) => DegreeCommandRegex().Replace(math, "^{\\circ}");

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

        /// <summary>A caret power after a letter, digit or closing bracket: <c>^{..}</c>, <c>^(-1)</c> or <c>^-1</c>/<c>^12</c>.</summary>
        [GeneratedRegex(@"(?<=[\p{L}\p{N})\]])\^(?:\{(?<power>[^{}\s]{1,12})\}|\((?<power>[-+\u2212]?\d+(?:\.\d+)?)\)|(?<power>[-+\u2212]?\d+(?:\.\d+)?))")]
        private static partial Regex CaretPowerRegex();

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex TagRegex();

        /// <summary><c>\degree</c> or <c>\textdegree</c>, optionally already raised (<c>^\degree</c>, <c>^{\degree}</c>).</summary>
        [GeneratedRegex(@"(?:\^\{\\(?:text)?degree\}|\^?\\(?:text)?degree(?![A-Za-z]))(?:\{\})?")]
        private static partial Regex DegreeCommandRegex();
    }
}
