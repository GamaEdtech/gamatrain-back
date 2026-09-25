namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    using AngleSharp.Dom;
    using AngleSharp.Html.Parser;

    using GamaEdtech.Data.Dto.Game;

    using W = ExamWordDocumentBuilder;

    /// <summary>
    /// Builds the Pdf export as HTML for Chromium's print engine, laid out to match the Word export
    /// (<see cref="ExamWordDocumentBuilder"/>) exactly: same page geometry, header, question grid, badges,
    /// separators, answer key and footer. Every measurement, color, layout rule and header shape comes from
    /// <see cref="ExamWordDocumentBuilder"/>'s own constants/helpers rather than a copy, so a style change there
    /// carries over here too -- a change to how something is *drawn* still has to be made in both places.
    /// Word measures in dxa (1/20pt); everything here is converted to CSS points, and pixel sizes (images) stay
    /// CSS px, which is the same 96dpi px <see cref="ExamWordDocumentBuilder"/> sizes pictures in.
    /// </summary>
    internal static partial class ExamPdfHtmlBuilder
    {
        /// <summary>Word's default table cell left/right margin (108dxa), which the Word export never overrides
        /// for its question/header tables.</summary>
        private const int DefaultCellHorizontalPaddingDxa = 108;

        /// <summary>Word's "single" line spacing for Times New Roman (the Word export's default font), as a
        /// multiple of the font size.</summary>
        private const double SingleLineHeight = 1.15;

        private const string FontFamily = "'Times New Roman', 'Liberation Serif', 'Tinos', serif";

        /// <summary>Chromium wraps each page template in its own container with a fixed padding (top for the
        /// header, bottom for the footer) -- nominally 0.4cm, measured at ~14.8pt in real output; the templates
        /// shift back by this much so their content lands exactly where the Word export's header/footer do.</summary>
        private const string ChromiumTemplatePadding = "14.8pt";

        /// <summary>The Word header's background/table start 1pt below the header distance -- its anchor paragraph's
        /// own pinned 1pt line (see <see cref="ExamWordDocumentBuilder"/>'s BuildHeaderBackgroundParagraph).</summary>
        private const int HeaderAnchorLineDxa = 20;

        /// <summary>Word silently leaves out a picture it can't download; hide a broken one the same way instead of
        /// Chromium's broken-image icon.</summary>
        private const string HideIfBroken = " onerror=\"this.style.display='none'\"";

        /// <summary>The finished page pieces for <c>IHeadlessBrowserRenderProvider.RenderPdfAsync</c>: the body
        /// document, Chromium's per-page header/footer templates, and the page margins they sit in.</summary>
        internal sealed record PdfPage(string BodyHtml, string HeaderTemplate, string FooterTemplate, string MarginTop, string MarginBottom, string MarginSide);

        public static async Task<PdfPage> BuildAsync(ExamInformationResponseDto data, HeaderBrandAssets brandAssets, string? watermarkText)
        {
            var body = new StringBuilder();
            _ = body.Append("<style>").Append(BuildStyles()).Append("</style>");

            if (!string.IsNullOrEmpty(watermarkText))
            {
                _ = body.Append(BuildWatermark(watermarkText));
            }

            if (data.Tests is not null)
            {
                for (var i = 0; i < data.Tests.Count; i++)
                {
                    _ = body.Append(await BuildQuestionAsync(data.Tests[i], i, data.Tests.Count));
                }

                _ = body.Append(await BuildAnswerKeyAsync(data.Tests));
            }

            return new PdfPage(
                body.ToString(),
                BuildHeaderTemplate(data.Exam, brandAssets),
                BuildFooterTemplate(brandAssets),
                Inches(W.PageMarginTopDxa),
                Inches(W.PageMarginBottomDxa),
                Inches(W.PageMarginLeftDxa));
        }

        /// <summary>Wraps the (formula-rendered) body HTML into the complete page document -- kept separate from
        /// <see cref="BuildAsync"/> so formula rendering, which returns a fragment, runs on the body alone.</summary>
        public static string WrapDocument(string bodyHtml) =>
            $"<!DOCTYPE html><html><head><meta charset=\"utf-8\" /></head><body>{bodyHtml}</body></html>";

        // Chromium's own page margins (unlike CSS) take only px/in/cm/mm, not pt.
        private static string Inches(double dxa) => (dxa / 1440d).ToString("0.#####", CultureInfo.InvariantCulture) + "in";

        /// <summary>
        /// The Pdf export's first page as a standalone single-page document, for the thumbnail
        /// (<c>ExportFileType.Thumbnail</c>): the same header/footer templates and formula-rendered body as the
        /// PDF, laid out on one A4 page (<see cref="ThumbnailPageWidthPx"/> x <see cref="ThumbnailPageHeightPx"/>
        /// CSS px) with the PDF's margins. Chromium's page-template padding that the templates compensate for is
        /// recreated around them, and a small script reproduces the PDF's first page break: the first question
        /// that doesn't fit entirely -- which the PDF moves to page 2 -- and everything after it are hidden. The
        /// footer shows "1 / <paramref name="pageCount"/>", the real PDF's page count.
        /// </summary>
        public static string BuildThumbnailDocument(PdfPage page, string bodyHtml, int pageCount)
        {
            var footer = page.FooterTemplate
                .Replace("<span class=\"pageNumber\"></span>", "1", StringComparison.Ordinal)
                .Replace("<span class=\"totalPages\"></span>", pageCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            const int contentHeightDxa = W.PageHeightDxa - W.PageMarginTopDxa - W.PageMarginBottomDxa;
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
                "<style>html,body{margin:0;padding:0;background:#fff;}.watermark{position:absolute !important;}</style></head>" +
                "<body data-pending=\"1\">" +
                $"<div style=\"position:relative;width:{Pt(W.PageWidthDxa)};height:{Pt(W.PageHeightDxa)};overflow:hidden;background:#fff;\">" +
                $"<div style=\"position:absolute;left:0;top:0;width:100%;padding-top:{ChromiumTemplatePadding};\">{page.HeaderTemplate}</div>" +
                $"<div id=\"content\" style=\"position:absolute;left:{Pt(W.PageMarginLeftDxa)};top:{Pt(W.PageMarginTopDxa)};width:{Pt(W.PageContentWidthDxa)};height:{Pt(contentHeightDxa)};overflow:hidden;\">{bodyHtml}</div>" +
                $"<div style=\"position:absolute;left:0;bottom:0;width:100%;padding-bottom:{ChromiumTemplatePadding};\">{footer}</div>" +
                "</div>" +
                "<script>window.addEventListener('load',function(){var c=document.getElementById('content');" +
                "var limit=c.getBoundingClientRect().bottom+0.5,hide=false;" +
                "Array.prototype.forEach.call(c.children,function(el){if(el.tagName==='STYLE'||el.classList.contains('watermark'))return;" +
                "if(hide||el.getBoundingClientRect().bottom>limit){hide=true;el.style.visibility='hidden';}});" +
                "document.body.dataset.ready='1';});</script>" +
                "</body></html>";
        }

        /// <summary>A4 in CSS px (96dpi): the thumbnail page's viewport.</summary>
        internal const int ThumbnailPageWidthPx = (W.PageWidthDxa + 14) / 15;

        internal const int ThumbnailPageHeightPx = (W.PageHeightDxa + 14) / 15;

        private static string Pt(double dxa) => (dxa / 20d).ToString("0.###", CultureInfo.InvariantCulture) + "pt";

        private static string PtFromHalfPoints(int halfPoints) => (halfPoints / 2d).ToString("0.###", CultureInfo.InvariantCulture) + "pt";

        private static string LineHeightPt(double fontSizePt) => (fontSizePt * SingleLineHeight).ToString("0.###", CultureInfo.InvariantCulture) + "pt";

        private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        private static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

        private static string BuildStyles()
        {
            var cellPadding = Pt(DefaultCellHorizontalPaddingDxa);

            // Word's gap between rows of answer blocks is a 12pt paragraph with 160dxa (8pt) after-spacing; the
            // answer-key slot's 6.9pt top padding is the empty paragraph Word puts above each block in its cell
            // (measured in the Word render).
            var answerKeyRowGap = LineHeightPt((W.AnswerKeyCellFontSizeHalfPoints / 2d) + (8 / SingleLineHeight));
            return string.Concat(
                "@page{size:A4;}",
                $"html,body{{margin:0;padding:0;font-family:{FontFamily};color:#{W.TextDark};-webkit-print-color-adjust:exact;print-color-adjust:exact;}}",
                "p{margin:0;}",
                "sup,sub{line-height:0;}",
                $"img.rich{{max-width:{W.MaxImageWidthPx}px;vertical-align:bottom;}}",
                "table.grid{border-collapse:collapse;table-layout:fixed;width:100%;}",
                $"table.grid td{{padding:0 {cellPadding};vertical-align:middle;}}",
                "table.grid td.num{vertical-align:top;}",
                $".question{{break-inside:avoid;page-break-inside:avoid;}}",
                $".qtext{{font-size:{PtFromHalfPoints(22)};line-height:{LineHeightPt(11)};font-weight:bold;}}",
                $".opt{{font-size:{PtFromHalfPoints(20)};line-height:{LineHeightPt(10)};}}",
                $".chip{{display:inline-block;background:#{W.BadgeGray};padding:{Pt(W.BadgeChipVerticalPaddingDxa)} {Pt(W.BadgeChipHorizontalPaddingDxa)};font-weight:bold;}}",
                $".chip.q{{font-size:{PtFromHalfPoints(22)};line-height:{LineHeightPt(11)};}}",
                $".chip.o{{font-size:{PtFromHalfPoints(18)};line-height:{LineHeightPt(9)};}}",
                ".center{text-align:center;}",
                $".sep{{height:{LineHeightPt(W.QuestionBottomPaddingFontSizeHalfPoints / 2d)};border-bottom:1pt solid #{W.SeparatorNavy};}}",
                $".gap{{height:{LineHeightPt(W.QuestionTopPaddingFontSizeHalfPoints / 2d)};}}",
                ".answer-key{break-before:page;page-break-before:always;}",
                $".ak-title{{font-size:16pt;font-weight:bold;line-height:{LineHeightPt(16)};}}",
                $".ak-subtitle{{font-size:14pt;font-weight:bold;line-height:{LineHeightPt(14)};margin:12pt 0 8pt 0;}}",
                $".ak-row{{display:flex;margin-bottom:{answerKeyRowGap};}}",
                $".ak-slot{{width:{Pt(W.AnswerKeyBlockContentWidthDxa + W.AnswerKeyGapColumnDxa)};padding:6.9pt {cellPadding} 0 {cellPadding};box-sizing:border-box;}}",
                $"table.ak{{border-collapse:collapse;table-layout:fixed;width:{Pt(W.AnswerKeyBlockContentWidthDxa)};font-size:{PtFromHalfPoints(W.AnswerKeyCellFontSizeHalfPoints)};line-height:{LineHeightPt(W.AnswerKeyCellFontSizeHalfPoints / 2d)};}}",
                $"table.ak td{{padding:0 {Pt(W.AnswerKeyCellHorizontalPaddingDxa)};text-align:center;border:none;}}",
                $"table.ak tr.head td{{background:#{W.AnswerKeyHeaderYellow};font-weight:bold;}}",
                $"table.ak td.n{{background:#{W.RowGrayBg};font-weight:bold;}}");
        }

        // ---- Questions ---------------------------------------------------------------------------------

        private static async Task<string> BuildQuestionAsync(ExamInformationResponseDto.TestDto test, int index, int totalCount)
        {
            var html = new StringBuilder();
            _ = html.Append("<div class=\"question\">").Append(GridTableStart())
                .Append("<tr><td class=\"num center\"><span class=\"chip q\">")
                .Append(index + 1)
                .Append(CultureInfo.InvariantCulture, $"</span></td><td class=\"qtext\" colspan=\"{W.ContentFineColumnCount}\">")
                .Append(await NormalizeRichTextAsync(test.Question))
                .Append("</td></tr>");

            if (test.HasOptions)
            {
                _ = html.Append(await BuildOptionRowsAsync(test));
            }
            else if (!string.IsNullOrEmpty(test.QuestionFile))
            {
                _ = html.Append(BuildCenteredFullWidthImageRow(test.QuestionFile));
            }

            // The separator stays inside the question's unbreakable block (moves with it to the next page); the
            // padding below it doesn't, same as the Word export, whose blank padding row is a separate table row.
            _ = html.Append("</table><div class=\"sep\"></div></div>");
            if (index < totalCount - 1)
            {
                _ = html.Append("<div class=\"gap\"></div>");
            }

            return html.ToString();
        }

        /// <summary>Opens the question grid table: the number-badge column plus the fine content columns, same as
        /// <see cref="ExamWordDocumentBuilder"/>'s shared question table.</summary>
        private static string GridTableStart()
        {
            var html = new StringBuilder("<table class=\"grid\"><colgroup>");
            _ = html.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{Pt(W.QuestionNumberColumnDxa)}\" />");
            for (var c = 0; c < W.ContentFineColumnCount; c++)
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{Pt(W.ContentFineColumnDxa)}\" />");
            }

            return html.Append("</colgroup>").ToString();
        }

        private static string BuildCenteredFullWidthImageRow(string src) =>
            $"<tr><td class=\"num\"></td><td class=\"center\" colspan=\"{W.ContentFineColumnCount}\"><img class=\"rich\" src=\"{Encode(src)}\"{HideIfBroken} /></td></tr>";

        private static async Task<string> BuildOptionRowsAsync(ExamInformationResponseDto.TestDto test)
        {
            var options = W.GetOptionModels(test);
            var layout = W.ClassifyLayout(test, options);
            var questionImageFile = layout == W.QuestionLayoutType.ImageOptionsHorizontal ? null : test.QuestionFile;
            var html = new StringBuilder();

            switch (layout)
            {
                case W.QuestionLayoutType.ImageOptionsHorizontal:
                    _ = html.Append("<tr><td class=\"num\"></td>");
                    foreach (var option in options)
                    {
                        _ = html.Append(BuildOptionBadgeCell(option.Number))
                            .Append(await BuildOptionContentCellAsync(null, option.File, 3, W.MaxImageOptionWidthPx));
                    }

                    _ = html.Append("</tr>");
                    break;

                case W.QuestionLayoutType.TextHorizontal:
                    if (!string.IsNullOrEmpty(questionImageFile))
                    {
                        _ = html.Append(BuildCenteredFullWidthImageRow(questionImageFile));
                    }

                    _ = html.Append("<tr><td class=\"num\"></td>");
                    foreach (var option in options)
                    {
                        _ = html.Append(BuildOptionBadgeCell(option.Number))
                            .Append(await BuildOptionContentCellAsync(option.Html, option.File, 3, null));
                    }

                    _ = html.Append("</tr>");
                    break;

                case W.QuestionLayoutType.TextVertical:
                {
                    var hasImage = !string.IsNullOrEmpty(questionImageFile);
                    var optionSpan = hasImage ? W.ContentFineColumnCount - W.ImageFineColumnSpan - 1 : W.ContentFineColumnCount - 1;
                    for (var i = 0; i < options.Length; i++)
                    {
                        _ = html.Append("<tr><td class=\"num\"></td>")
                            .Append(BuildOptionBadgeCell(options[i].Number))
                            .Append(await BuildOptionContentCellAsync(options[i].Html, options[i].File, optionSpan, null));
                        if (hasImage && i == 0)
                        {
                            _ = html.Append(BuildSharedImageCell(questionImageFile!, options.Length));
                        }

                        _ = html.Append("</tr>");
                    }

                    break;
                }

                default:
                {
                    var hasImage = !string.IsNullOrEmpty(questionImageFile);
                    var optionSpan = hasImage ? 5 : 7;
                    for (var i = 0; i < options.Length; i += 2)
                    {
                        _ = html.Append("<tr><td class=\"num\"></td>")
                            .Append(BuildOptionBadgeCell(options[i].Number))
                            .Append(await BuildOptionContentCellAsync(options[i].Html, options[i].File, optionSpan, null))
                            .Append(BuildOptionBadgeCell(options[i + 1].Number))
                            .Append(await BuildOptionContentCellAsync(options[i + 1].Html, options[i + 1].File, optionSpan, null));
                        if (hasImage && i == 0)
                        {
                            _ = html.Append(BuildSharedImageCell(questionImageFile!, options.Length / 2));
                        }

                        _ = html.Append("</tr>");
                    }

                    break;
                }
            }

            return html.ToString();
        }

        private static string BuildOptionBadgeCell(string number) =>
            $"<td class=\"center\"><span class=\"chip o\">{Encode(number)}</span></td>";

        private static async Task<string> BuildOptionContentCellAsync(string? optionHtml, string? optionFile, int span, int? imageMaxWidthPx)
        {
            var html = new StringBuilder();
            _ = html.Append(CultureInfo.InvariantCulture, $"<td class=\"opt\" colspan=\"{span}\">").Append(await NormalizeRichTextAsync(optionHtml));
            if (!string.IsNullOrEmpty(optionFile))
            {
                var width = imageMaxWidthPx is null ? string.Empty : $" style=\"max-width:{imageMaxWidthPx.Value.ToString(CultureInfo.InvariantCulture)}px\"";
                _ = html.Append(CultureInfo.InvariantCulture, $"<p><img class=\"rich\" src=\"{Encode(optionFile)}\"{width}{HideIfBroken} /></p>");
            }

            return html.Append("</td>").ToString();
        }

        /// <summary>Text2x2/TextVertical's shared question image: the last <see cref="ExamWordDocumentBuilder.ImageFineColumnSpan"/>
        /// columns, spanning every option row (Word's <c>w:vMerge</c>), at a fixed
        /// <see cref="ExamWordDocumentBuilder.MaxQuestionSideImageWidthPx"/> width.</summary>
        private static string BuildSharedImageCell(string src, int rowSpan) =>
            string.Create(CultureInfo.InvariantCulture,
                $"<td class=\"center\" colspan=\"{W.ImageFineColumnSpan}\" rowspan=\"{rowSpan}\"><img class=\"rich\" src=\"{Encode(src)}\" style=\"width:{W.MaxQuestionSideImageWidthPx}px\"{HideIfBroken} /></td>");

        /// <summary>
        /// Reduces Core's rich-text HTML to exactly what <see cref="ExamWordRichText"/> keeps in Word, so both
        /// exports show the same content the same way: each top-level &lt;p&gt;/&lt;div&gt; becomes one paragraph
        /// (with none, the whole fragment is one paragraph); inside, only line breaks, images, bold/italic/
        /// underline, superscript/subscript and a CSS text color survive -- any other markup (inline font sizes,
        /// nested tables, ...) is flattened to its text, as in Word. <c>$...$</c> formulas pass through as text for
        /// MathJax to render afterwards.
        /// </summary>
        private static async Task<string> NormalizeRichTextAsync(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return "<p>&#8203;</p>";
            }

            var document = await new HtmlParser().ParseDocumentAsync($"<body>{html}</body>");
            var body = document.Body!;
            var blocks = body.Children.Where(t => t.NodeName is "P" or "DIV").ToList();
            var output = new StringBuilder();
            if (blocks.Count == 0)
            {
                return output.Append("<p>").Append(NodesToHtml(body.ChildNodes)).Append("</p>").ToString();
            }

            foreach (var block in blocks)
            {
                _ = output.Append("<p>").Append(NodesToHtml(block.ChildNodes)).Append("</p>");
            }

            return output.ToString();
        }

        /// <summary>Word shows inline equations at full size (a fraction's numerator/denominator at the text's
        /// own size); MathJax's inline math shrinks them. <c>\displaystyle</c> makes each <c>$...$</c> formula
        /// render the way Word shows it.</summary>
        private static string DisplayStyleFormulas(string text) => InlineFormulaRegex().Replace(text, @"$$\displaystyle $1$$");

        [GeneratedRegex(@"(?<!\$)\$(?!\$)([^$]+)\$")]
        private static partial Regex InlineFormulaRegex();

        private static string NodesToHtml(INodeList nodes) => string.Concat(nodes.Select(NodeToHtml));

        private static string NodeToHtml(INode node) => node switch
        {
            { NodeType: NodeType.Text } => DisplayStyleFormulas(Encode(node.TextContent)),
            IElement element => ElementToHtml(element),
            _ => string.Empty,
        };

        private static string ElementToHtml(IElement element) => element.NodeName switch
        {
            "BR" => "<br />",
            "IMG" => string.IsNullOrEmpty(element.GetAttribute("src")) ? string.Empty : $"<img class=\"rich\" src=\"{Encode(element.GetAttribute("src"))}\"{HideIfBroken} />",
            "B" or "STRONG" => $"<b>{NodesToHtml(element.ChildNodes)}</b>",
            "I" or "EM" => $"<i>{NodesToHtml(element.ChildNodes)}</i>",
            "U" => $"<u>{NodesToHtml(element.ChildNodes)}</u>",
            "SUP" => $"<sup>{NodesToHtml(element.ChildNodes)}</sup>",
            "SUB" => $"<sub>{NodesToHtml(element.ChildNodes)}</sub>",
            "SPAN" or "P" or "DIV" => ExamWordRichText.ExtractCssColor(element.GetAttribute("style")) is { } color
                ? $"<span style=\"color:#{color}\">{NodesToHtml(element.ChildNodes)}</span>"
                : NodesToHtml(element.ChildNodes),
            _ => NodesToHtml(element.ChildNodes),
        };

        // ---- Answer key --------------------------------------------------------------------------------

        private static async Task<string> BuildAnswerKeyAsync(List<ExamInformationResponseDto.TestDto> tests)
        {
            // Same rules as the Word export: the multiple-choice grid only when there are multiple-choice questions,
            // then the descriptive answers, and no page at all when there's neither.
            var hasChoiceQuestions = tests.Any(t => t.HasOptions);
            var descriptiveAnswers = W.DescriptiveAnswers(tests);
            if (!hasChoiceQuestions && descriptiveAnswers.Count == 0)
            {
                return string.Empty;
            }

            var html = new StringBuilder();
            // 3.3pt: where Word's own spacer + tight-line title land the heading (measured against the Word render).
            _ = html.Append(CultureInfo.InvariantCulture, $"<div class=\"answer-key\"><div style=\"height:3.3pt\"></div><div class=\"ak-title\">Answer Key</div>");

            var blockCount = hasChoiceQuestions ? (int)Math.Ceiling(tests.Count / (double)W.AnswerKeyRowsPerBlock) : 0;
            for (var blockStart = 0; blockStart < blockCount; blockStart += W.AnswerKeyBlocksPerRow)
            {
                _ = html.Append("<div class=\"ak-row\">");
                var blocksInThisRow = Math.Min(W.AnswerKeyBlocksPerRow, blockCount - blockStart);
                for (var b = 0; b < blocksInThisRow; b++)
                {
                    var questionStart = (blockStart + b) * W.AnswerKeyRowsPerBlock;
                    var questionEnd = Math.Min(questionStart + W.AnswerKeyRowsPerBlock, tests.Count);
                    _ = html.Append("<div class=\"ak-slot\">").Append(BuildAnswerKeyBlock(tests, questionStart, questionEnd)).Append("</div>");
                }

                _ = html.Append("</div>");
            }

            if (descriptiveAnswers.Count > 0)
            {
                if (hasChoiceQuestions)
                {
                    _ = html.Append("<div class=\"ak-subtitle\">Descriptive Answers</div>");
                }

                for (var i = 0; i < descriptiveAnswers.Count; i++)
                {
                    var (number, test) = descriptiveAnswers[i];
                    _ = html.Append("<div class=\"question\">").Append(GridTableStart())
                        .Append(CultureInfo.InvariantCulture, $"<tr><td class=\"num center\"><span class=\"chip q\">{number}</span></td><td class=\"opt\" colspan=\"{W.ContentFineColumnCount}\">")
                        .Append(await NormalizeRichTextAsync(test.AnswerHtml))
                        .Append("</td></tr>");
                    if (!string.IsNullOrEmpty(test.AnswerFile))
                    {
                        _ = html.Append(BuildCenteredFullWidthImageRow(test.AnswerFile));
                    }

                    _ = html.Append("</table><div class=\"sep\"></div></div>");
                    if (i < descriptiveAnswers.Count - 1)
                    {
                        _ = html.Append("<div class=\"gap\"></div>");
                    }
                }
            }

            return html.Append("</div>").ToString();
        }

        /// <summary>Same border pattern as <see cref="ExamWordDocumentBuilder"/>'s answer-key block: an outer box,
        /// a divider between the number and option columns on question rows only, no other inner lines.</summary>
        private static string BuildAnswerKeyBlock(List<ExamInformationResponseDto.TestDto> tests, int questionStart, int questionEnd)
        {
            var border = $"0.5pt solid #{W.AnswerKeyBorderColor}";
            var html = new StringBuilder();
            _ = html.Append("<table class=\"ak\"><colgroup>")
                .Append(CultureInfo.InvariantCulture, $"<col style=\"width:{Pt(W.AnswerKeyNumberColumnDxa)}\" />");
            for (var c = 0; c < 4; c++)
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{Pt(W.AnswerKeyOptionColumnDxa)}\" />");
            }

            _ = html.Append("</colgroup>");

            string CellStyle(int column, bool isHeader, bool isLastRow)
            {
                var style = new StringBuilder();
                if (column == 0)
                {
                    _ = style.Append(CultureInfo.InvariantCulture, $"border-left:{border};");
                }

                if (column == 4)
                {
                    _ = style.Append(CultureInfo.InvariantCulture, $"border-right:{border};");
                }

                if (!isHeader && column == 0)
                {
                    _ = style.Append(CultureInfo.InvariantCulture, $"border-right:{border};");
                }

                if (isHeader)
                {
                    _ = style.Append(CultureInfo.InvariantCulture, $"border-top:{border};");
                }

                if (isLastRow)
                {
                    _ = style.Append(CultureInfo.InvariantCulture, $"border-bottom:{border};");
                }

                return style.ToString();
            }

            var totalRows = questionEnd - questionStart;
            _ = html.Append("<tr class=\"head\">");
            for (var col = 0; col < 5; col++)
            {
                var text = col == 0 ? string.Empty : col.ToString(CultureInfo.InvariantCulture);
                _ = html.Append(CultureInfo.InvariantCulture, $"<td style=\"{CellStyle(col, true, totalRows == 0)}\">{text}</td>");
            }

            _ = html.Append("</tr>");

            var optionLetters = new[] { 'A', 'B', 'C', 'D' };
            for (var i = questionStart; i < questionEnd; i++)
            {
                var isLastRow = i == questionEnd - 1;
                var correct = char.ToUpperInvariant(tests[i].CorrectOption ?? ' ');
                _ = html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"n\" style=\"{CellStyle(0, false, isLastRow)}\">{i + 1}</td>");
                for (var col = 0; col < optionLetters.Length; col++)
                {
                    var mark = correct == optionLetters[col] ? "■" : "□";
                    _ = html.Append(CultureInfo.InvariantCulture, $"<td style=\"{CellStyle(col + 1, false, isLastRow)}\">{mark}</td>");
                }

                _ = html.Append("</tr>");
            }

            return html.Append("</table>").ToString();
        }

        // ---- Watermark -----------------------------------------------------------------------------------

        /// <summary>Same watermark as the Word export's VML text path: 415x207.5pt, rotated 315 degrees, centered
        /// on the content area, #4472C4 at 50% opacity, text stretched to fill the box. <c>position:fixed</c> makes
        /// Chromium repeat it on every printed page.</summary>
        private static string BuildWatermark(string text) =>
            "<div class=\"watermark\" style=\"position:fixed;top:50%;left:50%;width:415pt;height:207.5pt;transform:translate(-50%,-50%) rotate(-45deg);z-index:-1;opacity:0.5;\">" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"100%\" viewBox=\"0 0 415 207.5\" preserveAspectRatio=\"none\">" +
            $"<text x=\"0\" y=\"160\" font-family=\"Calibri, Carlito, sans-serif\" font-size=\"200\" fill=\"#4472C4\" textLength=\"415\" lengthAdjust=\"spacingAndGlyphs\">{Encode(text)}</text>" +
            "</svg></div>";

        // ---- Header / footer (Chromium page templates) --------------------------------------------------

        /// <summary>
        /// The page header: the shared background shapes (<see cref="ExamWordDocumentBuilder.HeaderBackgroundShapes"/>)
        /// as inline SVG, with the same 20-column header table on top, sized exactly like
        /// <see cref="ExamWordDocumentBuilder"/>'s header table (brand row the panel band's height, metadata row fixed,
        /// title row taking the rest). Chromium renders page templates in isolation -- no page CSS, no external
        /// files -- so everything is inline and every picture is a data URI.
        /// </summary>
        private static string BuildHeaderTemplate(ExamInformationResponseDto.ExamDto? exam, HeaderBrandAssets brandAssets)
        {
            const int columnCount = 20;
            const int columnUnitDxa = W.PageContentWidthDxa / columnCount;
            const int lastColumnDxa = W.PageContentWidthDxa - (columnUnitDxa * (columnCount - 1));
            var columnWidths = Enumerable.Repeat(columnUnitDxa, columnCount - 1).Append(lastColumnDxa).ToArray();
            var line = $"0.5pt solid #{W.BorderLightGray}";
            var cellPadding = Pt(DefaultCellHorizontalPaddingDxa);

            string Cell(string content, int span, string align, string verticalAlign, bool top, bool left, bool right, bool bottom, string? extraStyle = null) =>
                string.Create(CultureInfo.InvariantCulture,
                    $"<td colspan=\"{span}\" style=\"padding:0 {cellPadding};text-align:{align};vertical-align:{verticalAlign};" +
                    $"border-top:{(top ? line : "none")};border-left:{(left ? line : "none")};border-right:{(right ? line : "none")};border-bottom:{(bottom ? line : "none")};{extraStyle}\">{content}</td>");

            var text10 = $"font-size:10pt;line-height:{LineHeightPt(10)};";
            var html = new StringBuilder();
            _ = html.Append(CultureInfo.InvariantCulture,
                $"<div style=\"width:100%;height:{Pt(W.PageMarginTopDxa)};position:relative;margin:0;font-family:{FontFamily};color:#{W.TextDark};-webkit-print-color-adjust:exact;print-color-adjust:exact;\">")
                .Append(CultureInfo.InvariantCulture,
                $"<div style=\"position:absolute;left:{Pt(W.PageMarginLeftDxa)};top:calc({Pt(W.PageMarginHeaderDxa + HeaderAnchorLineDxa)} - {ChromiumTemplatePadding});width:{Pt(W.PageContentWidthDxa)};height:{Pt(W.HeaderBackgroundHeightDxa)};\">")
                .Append(BuildHeaderBackgroundSvg())
                .Append(CultureInfo.InvariantCulture,
                $"<table style=\"position:absolute;left:0;top:0;width:{Pt(W.PageContentWidthDxa)};border-collapse:collapse;table-layout:fixed;{text10}\"><colgroup>");
            foreach (var width in columnWidths)
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{Pt(width)}\" />");
            }

            _ = html.Append("</colgroup>");

            // Brand row: only a bottom line, so the logo/portrait/By:/QR read as one bar on the background.
            var qrImage = string.IsNullOrEmpty(exam?.QrCode) ? string.Empty : $"<img src=\"{Encode(exam.QrCode)}\" style=\"width:50px;height:50px;display:inline-block;vertical-align:middle;\" />";
            _ = html.Append(CultureInfo.InvariantCulture, $"<tr style=\"height:{Pt(W.HeaderBrandRowHeightDxa)}\">")
                .Append(Cell($"<img src=\"data:image/svg+xml;base64,{Convert.ToBase64String(brandAssets.GamaWordmarkSvg)}\" style=\"width:270px;height:52px;display:block;\" />", 9, "left", "top", false, false, false, true, "padding-left:0;"))
                .Append(Cell($"<img src=\"{DataUri(brandAssets.ProfilePlaceholder)}\" style=\"width:45px;height:45px;display:inline-block;vertical-align:middle;\" />", 2, "center", "middle", false, false, false, true))
                .Append(Cell($"By: <b>{Encode(exam?.Author)}</b>", 5, "left", "middle", false, false, false, true))
                .Append(Cell(qrImage, 4, "right", "middle", false, false, false, true, "padding-right:7.5pt;"))
                .Append("</tr>");

            var titleRowMinDxa = W.HeaderBackgroundHeightDxa - W.HeaderBrandRowHeightDxa - W.HeaderMetadataRowHeightDxa - W.HeaderRowBordersAllowanceDxa;
            _ = html.Append(CultureInfo.InvariantCulture, $"<tr style=\"height:{Pt(titleRowMinDxa)}\">")
                .Append(Cell(Encode(exam?.Title), 15, "left", "middle", true, false, true, true, $"font-size:12pt;line-height:{LineHeightPt(12)};font-weight:bold;"))
                .Append(Cell("Date:", 2, "left", "middle", true, true, true, true))
                .Append(Cell(Encode(exam?.StartDate), 3, "left", "middle", true, true, false, true))
                .Append("</tr>");

            _ = html.Append(CultureInfo.InvariantCulture, $"<tr style=\"height:{Pt(W.HeaderMetadataRowHeightDxa)}\">")
                .Append(Cell("Name:", 4, "left", "middle", true, false, true, false))
                .Append(Cell("School:", 4, "left", "middle", true, true, true, false))
                .Append(Cell($"Questions: <b>{(exam?.TestsCount ?? 0).ToString(CultureInfo.InvariantCulture)}</b>", 4, "left", "middle", true, true, true, false))
                .Append(Cell($"Time: <b>{Encode(exam?.ExamTime)} min</b>", 4, "left", "middle", true, true, true, false))
                .Append(Cell($"Level: <b>{Encode(exam?.Level)}</b>", 4, "left", "middle", true, true, false, false))
                .Append("</tr></table></div></div>");

            return html.ToString();
        }

        private static string BuildHeaderBackgroundSvg()
        {
            var svg = new StringBuilder();
            _ = svg.Append(CultureInfo.InvariantCulture,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" style=\"position:absolute;left:0;top:0;overflow:visible;\" width=\"{Pt(W.PageContentWidthDxa)}\" height=\"{Pt(W.HeaderBackgroundHeightDxa)}\" viewBox=\"0 0 {W.HeaderBackgroundReferenceWidth} {W.HeaderBackgroundReferenceHeight}\" preserveAspectRatio=\"none\">");
            foreach (var shape in W.HeaderBackgroundShapes)
            {
                var d = string.Join(' ', shape.Commands.Select(t => t.Kind + string.Join(',', t.Coordinates.Select(c => c.ToString(CultureInfo.InvariantCulture)))));
                _ = shape.FillHex is not null
                    ? svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"#{shape.FillHex}\" />")
                    : svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"none\" stroke=\"#{shape.StrokeHex}\" stroke-width=\"0.5pt\" vector-effect=\"non-scaling-stroke\" />");
            }

            return svg.Append("</svg>").ToString();
        }

        /// <summary>Same footer as the Word export: "page / pages" on the left, the globe icon plus the linked
        /// "www.gamatrain.com" centered, and the wave shape centered at the very bottom of the page.</summary>
        private static string BuildFooterTemplate(HeaderBrandAssets brandAssets) =>
                $"<div style=\"width:100%;height:{Pt(W.PageMarginBottomDxa)};margin:0 0 -{ChromiumTemplatePadding} 0;padding:0 {Pt(W.PageMarginRightDxa)} 0 {Pt(W.PageMarginLeftDxa)};box-sizing:border-box;display:flex;flex-direction:column;justify-content:flex-end;font-family:{FontFamily};font-size:8pt;-webkit-print-color-adjust:exact;print-color-adjust:exact;\">" +
                "<div style=\"display:flex;align-items:center;\">" +
                $"<div style=\"flex:1;color:#{W.TextMuted};padding-left:{Pt(DefaultCellHorizontalPaddingDxa)};\"><span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></div>" +
                $"<div style=\"flex:1;text-align:center;\"><a href=\"{W.GamatrainWebsiteUrl}\" style=\"color:#{W.TextDark};font-weight:bold;text-decoration:none;display:inline-flex;align-items:center;gap:3pt;\">" +
                $"<img src=\"{DataUri(brandAssets.FooterGlobe)}\" style=\"width:14px;height:14px;\" />www.gamatrain.com</a></div>" +
                "<div style=\"flex:1;\"></div></div>" +
                $"<div style=\"text-align:center;line-height:0;\"><img src=\"{DataUri(brandAssets.FooterWave)}\" style=\"width:60px;height:20px;\" /></div>" +
                "</div>";
    }
}
