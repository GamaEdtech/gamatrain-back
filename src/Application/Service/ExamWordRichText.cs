namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;

    using AngleSharp.Dom;
    using AngleSharp.Html.Parser;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;

    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using OoxmlMath = DocumentFormat.OpenXml.Math;

    /// <summary>
    /// Converts a question/option HTML fragment (as returned by Core -- rich text, typically one or more
    /// &lt;p&gt; blocks, possibly with &lt;b&gt;/&lt;i&gt;/&lt;span style="color:..."&gt;/&lt;br&gt;/&lt;img&gt;,
    /// plus MathJax formula &lt;img&gt; tags already substituted in by <c>IHeadlessBrowserRenderProvider
    /// .RenderFormulasAsync</c>) directly into native OOXML paragraphs/runs/images -- no HtmlToOpenXml
    /// conversion layer involved anywhere in this. Every visual property is set directly by
    /// <see cref="ExamWordDocumentBuilder"/>'s callers, not translated from CSS.
    /// </summary>
    internal static class ExamWordRichText
    {
        /// <summary>
        /// Parses <paramref name="html"/> (a fragment, not a full document) into OOXML paragraphs. Each
        /// top-level block element (&lt;p&gt;, &lt;div&gt;) becomes one <see cref="Ooxml.Paragraph"/>; a
        /// fragment with no block elements at all becomes a single paragraph.
        /// </summary>
        public static async Task<List<Ooxml.Paragraph>> ParseToParagraphsAsync(
            string? html, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, RunFormat baseFormat)
        {
            var paragraphs = new List<Ooxml.Paragraph>();
            if (string.IsNullOrWhiteSpace(html))
            {
                paragraphs.Add(new Ooxml.Paragraph());
                return paragraphs;
            }

            var document = await new HtmlParser().ParseDocumentAsync($"<body>{html}</body>");
            var body = document.Body!;

            var blocks = body.Children.Where(t => t.NodeName is "P" or "DIV").ToList();
            if (blocks.Count == 0)
            {
                var paragraph = new Ooxml.Paragraph();
                foreach (var run in await NodesToRunsAsync(body.ChildNodes, mainPart, httpClient, baseFormat))
                {
                    _ = paragraph.AppendChild(run);
                }

                paragraphs.Add(paragraph);
                return paragraphs;
            }

            foreach (var block in blocks)
            {
                var paragraph = new Ooxml.Paragraph();
                foreach (var run in await NodesToRunsAsync(block.ChildNodes, mainPart, httpClient, baseFormat))
                {
                    _ = paragraph.AppendChild(run);
                }

                paragraphs.Add(paragraph);
            }

            return paragraphs;
        }

        private static async Task<List<OpenXmlElement>> NodesToRunsAsync(
            INodeList nodes, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, RunFormat format)
        {
            var elements = new List<OpenXmlElement>();
            foreach (var node in nodes)
            {
                elements.AddRange(await NodeToRunsAsync(node, mainPart, httpClient, format));
            }

            return elements;
        }

        private static async Task<List<OpenXmlElement>> NodeToRunsAsync(
            INode node, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, RunFormat format)
        {
            if (node.NodeType == NodeType.Text)
            {
                var text = node.TextContent;
                return string.IsNullOrEmpty(text) ? [] : [BuildTextRun(text, format)];
            }

            if (node is not IElement element)
            {
                return [];
            }

            switch (element.NodeName)
            {
                case "BR":
                    var breakRun = new Ooxml.Run();
                    _ = breakRun.AppendChild(new Ooxml.Break());
                    return [breakRun];

                case "IMG":
                    var src = element.GetAttribute("src");
                    if (string.IsNullOrEmpty(src))
                    {
                        return [];
                    }

                    var drawing = await ExamWordDocumentBuilder.EmbedImageFromSourceAsync(mainPart, src, httpClient, null, null);
                    if (drawing is null)
                    {
                        return [];
                    }

                    var imageRun = new Ooxml.Run();
                    _ = imageRun.AppendChild(drawing);
                    return [imageRun];

                case "B" or "STRONG":
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format with { Bold = true });

                case "I" or "EM":
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format with { Italic = true });

                case "U":
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format with { Underline = true });

                case "SUP":
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format with { Superscript = true });

                case "SUB":
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format with { Subscript = true });

                case "SPAN" when element.HasAttribute("data-omml-b64"):
                    var ommlMarker = DecodeOmmlMarker(element.GetAttribute("data-omml-b64"));
                    return ommlMarker is null ? [] : [ommlMarker];

                case "SPAN" or "P" or "DIV":
                    var color = ExtractCssColor(element.GetAttribute("style"));
                    var nested = format;
                    if (color is not null)
                    {
                        nested = nested with { ColorHex = color };
                    }

                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, nested);

                default:
                    return await NodesToRunsAsync(element.ChildNodes, mainPart, httpClient, format);
            }
        }

        /// <summary>
        /// Decodes a <c>data-omml-b64</c> marker (see <c>HeadlessBrowserRenderProvider.RenderFormulasToOmmlAsync</c>)
        /// into a native <c>m:oMath</c> element, a direct sibling of <see cref="Ooxml.Run"/> within the
        /// paragraph -- not wrapped in a run, since OOXML math content lives at that level, same as Word's
        /// own inline equation objects. Returns <see langword="null"/> on any malformed input rather than
        /// throwing: one bad formula should drop silently, not fail the whole document.
        /// </summary>
        private static OoxmlMath.OfficeMath? DecodeOmmlMarker(string? base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }

            try
            {
                var xml = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return new OoxmlMath.OfficeMath(xml);
            }
            catch (Exception exc) when (exc is FormatException or System.Xml.XmlException)
            {
                return null;
            }
        }

        // CT_RPr child order (ECMA-376 17.3.2): b, i, ..., color, spacing, w, kern, position, sz, szCs,
        // highlight, u, effect, bdr, shd, fitText, vertAlign, ... -- u/vertAlign must come after
        // color/sz, not before, or Word's schema validator rejects the element on load.
        private static Ooxml.Run BuildTextRun(string text, RunFormat format)
        {
            var run = new Ooxml.Run();
            var runProperties = new Ooxml.RunProperties();
            if (format.Bold)
            {
                _ = runProperties.AppendChild(new Ooxml.Bold());
            }

            if (format.Italic)
            {
                _ = runProperties.AppendChild(new Ooxml.Italic());
            }

            _ = runProperties.AppendChild(new Ooxml.Color { Val = format.ColorHex });
            _ = runProperties.AppendChild(new Ooxml.FontSize { Val = format.FontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });

            if (format.Underline)
            {
                _ = runProperties.AppendChild(new Ooxml.Underline { Val = Ooxml.UnderlineValues.Single });
            }

            if (format.Superscript)
            {
                _ = runProperties.AppendChild(new Ooxml.VerticalTextAlignment { Val = Ooxml.VerticalPositionValues.Superscript });
            }

            if (format.Subscript)
            {
                _ = runProperties.AppendChild(new Ooxml.VerticalTextAlignment { Val = Ooxml.VerticalPositionValues.Subscript });
            }

            _ = run.AppendChild(runProperties);
            _ = run.AppendChild(new Ooxml.Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return run;
        }

        /// <summary>Reads a simple <c>color:#rrggbb</c> declaration out of an inline style attribute.</summary>
        private static string? ExtractCssColor(string? style)
        {
            if (string.IsNullOrEmpty(style))
            {
                return null;
            }

            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = declaration.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Equals("color", StringComparison.OrdinalIgnoreCase))
                {
                    var value = parts[1].TrimStart('#');
                    return value.Length is 6 or 3 ? value.ToUpperInvariant() : null;
                }
            }

            return null;
        }

        /// <summary>Text run formatting state threaded through recursive HTML-node walking (bold/italic/etc accumulate as nested inline tags are entered).</summary>
        internal readonly record struct RunFormat(bool Bold, bool Italic, bool Underline, bool Superscript, bool Subscript, string ColorHex, int FontSizeHalfPoints);
    }
}
