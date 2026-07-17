namespace GamaEdtech.Application.Service
{
    using AngleSharp.Html.Parser;

    /// <summary>
    /// Strips a Core rich-text HTML fragment down to plain text for the PowerPoint export, whose slides
    /// use plain DrawingML text runs rather than the paragraph/run-level rich text
    /// <see cref="ExamWordRichText"/> builds for Word. A rendered MathJax formula (an &lt;img&gt; by the
    /// time this runs) has no plain-text equivalent and is dropped -- PowerPoint text shapes can't host an
    /// inline image the way a Word run can, so formula rendering in the PPTX export is a known gap.
    /// </summary>
    internal static class ExamRichTextPlain
    {
        public static string ToPlainText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
            return document.Body?.TextContent.Trim() ?? string.Empty;
        }
    }
}
