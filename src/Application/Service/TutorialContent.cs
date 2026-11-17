namespace GamaEdtech.Application.Service
{
    using System.Net;
    using System.Text.RegularExpressions;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// A tutorial's HTML, shared by the website's page and the admin: gama-api's image links pointed at this backend's
    /// storage and the HTML sanitized; the description made from its headings (as gama-api does); its plain text for the AI.
    /// See docs/business/tutorials.md.
    /// </summary>
    internal static partial class TutorialContent
    {
        internal const int DescriptionLength = 1000;

        /// <summary>The stored HTML as it is shown and edited: gama-api's image links point at the <c>Tutorial</c> container and it
        /// is sanitized as every stored HTML is (no scripts, handlers or frames).</summary>
        internal static string ForPage(string? content, IFileService fileService)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            var probe = fileService.GetStaticFileUrl(new() { FileId = "x", ContainerType = ContainerType.Tutorial });
            var imagesBase = string.IsNullOrEmpty(probe) ? null : probe[..^1];
            var rewritten = imagesBase is null ? content : TutorialImageRegex().Replace(content, imagesBase);
            return rewritten.SanitizeHtml() ?? string.Empty;
        }

        /// <summary>gama-api's <c>description_gen</c>: the text of the <c>h2</c> and <c>h3</c> headings joined by " | " (the page
        /// description lists them), cut to the column.</summary>
        internal static string? Description(string? content)
        {
            List<string> headings = [];
            foreach (Match match in HeadingRegex().Matches(content ?? string.Empty))
            {
                var text = SpacesRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(match.Groups[1].Value, " ")), " ").Trim();
                if (text.Length > 0)
                {
                    headings.Add(text);
                }
            }

            var description = string.Join(" | ", headings);
            return description.Length switch
            {
                0 => null,
                > DescriptionLength => description[..DescriptionLength],
                _ => description,
            };
        }

        /// <summary>The text of the HTML (no scripts, styles or embedded data), for the AI, cut to <paramref name="maxLength"/>.</summary>
        internal static string PlainText(string? content, int maxLength)
        {
            var html = NonTextRegex().Replace(content ?? string.Empty, " ");
            html = BlockEndRegex().Replace(html, "\n");
            var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
            text = string.Join('\n', text.Split('\n').Select(t => SpacesRegex().Replace(t, " ").Trim()).Where(t => t.Length > 0));
            return text.Length > maxLength ? text[..maxLength] : text;
        }

        [GeneratedRegex(@"https?://(?:core\.)?gamatrain\.com/uploads/tutorials/", RegexOptions.IgnoreCase)]
        private static partial Regex TutorialImageRegex();

        [GeneratedRegex(@"<h[23]\b[^>]*>(.*?)</h[23]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex HeadingRegex();

        [GeneratedRegex(@"<(script|style|noscript|svg|iframe|object)\b.*?</\1\s*>|data:[a-z]+/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex NonTextRegex();

        [GeneratedRegex(@"</(p|div|li|h[1-6]|tr|table|ul|ol|blockquote)\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex BlockEndRegex();

        [GeneratedRegex("<[^>]*>")]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"[\s ]+")]
        private static partial Regex SpacesRegex();
    }
}
