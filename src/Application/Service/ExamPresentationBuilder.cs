namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using AngleSharp.Dom;
    using AngleSharp.Html.Parser;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Data.Dto.Game;

    using SkiaSharp;

    using P = DocumentFormat.OpenXml.Presentation;
    using A = DocumentFormat.OpenXml.Drawing;

    /// <summary>
    /// Builds an exam PowerPoint deck entirely via native OOXML (PresentationML) -- no Spire, no HTML
    /// importer. One title/summary slide, then one slide per question, matching the same navy/yellow
    /// gamatrain design as <see cref="ExamWordDocumentBuilder"/> (same colors, same badge-cell pattern
    /// for options) translated to PowerPoint's absolutely-positioned shape model instead of flowing tables.
    /// </summary>
    internal static class ExamPresentationBuilder
    {
        private const long EmuPerPixel = 9525;
        private const int SlideWidth = 12192000; // 16:9, 13.333in
        private const int SlideHeight = 6858000; // 7.5in
        private const long Margin = 400000;

        private const string NavyDark = "172437";
        private const string NavyMid = "21324A";
        private const string NavyBadge = "202C3E";
        private const string Yellow = "F6B500";
        private const string BorderGray = "9AABBA";
        private const string RowGrayBg = "F4F7FB";
        private const string TextDark = "172033";
        private const string TextMuted = "5B6777";
        private const string TextTagline = "D5DDE8";

        private static uint shapeIdCounter = 1;

        public static async Task<byte[]> BuildAsync(
            [NotNull] ExamInformationResponseDto data, byte[] logoBytes, Lazy<HttpClient> httpClient)
        {
            using MemoryStream stream = new();
            using (var package = PresentationDocument.Create(stream, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
            {
                var presentationPart = package.AddPresentationPart();
                presentationPart.Presentation = new P.Presentation();

                var themePart = BuildThemePart(presentationPart);
                var slideLayoutPart = BuildSlideLayoutPart(BuildSlideMasterPart(presentationPart, themePart));

                var slideIdList = new P.SlideIdList();
                uint slideId = 256;

                var titleSlidePart = await BuildTitleSlideAsync(presentationPart, slideLayoutPart, data.Exam, logoBytes);
                _ = slideIdList.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(titleSlidePart) });

                if (data.Tests is not null)
                {
                    for (var i = 0; i < data.Tests.Count; i++)
                    {
                        var questionSlidePart = await BuildQuestionSlideAsync(presentationPart, slideLayoutPart, data.Tests[i], i, httpClient);
                        _ = slideIdList.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(questionSlidePart) });
                    }
                }

                var slideMasterId = new P.SlideMasterId { Id = 2147483648, RelationshipId = presentationPart.GetIdOfPart(presentationPart.SlideMasterParts.First()) };
                var slideMasterIdList = new P.SlideMasterIdList();
                _ = slideMasterIdList.AppendChild(slideMasterId);

                _ = presentationPart.Presentation.AppendChild(slideMasterIdList);
                _ = presentationPart.Presentation.AppendChild(slideIdList);
                _ = presentationPart.Presentation.AppendChild(new P.SlideSize { Cx = SlideWidth, Cy = SlideHeight });
                _ = presentationPart.Presentation.AppendChild(new P.NotesSize { Cx = 6858000, Cy = 9144000 });

                presentationPart.Presentation.Save();
            }

            return stream.ToArray();
        }

        // ---- Title slide --------------------------------------------------------------------------------

        private static async Task<SlidePart> BuildTitleSlideAsync(
            PresentationPart presentationPart, SlideLayoutPart slideLayoutPart,
            ExamInformationResponseDto.ExamDto? exam, byte[] logoBytes)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            _ = slidePart.AddPart(slideLayoutPart);

            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);

            const long headerHeight = 1400000;
            _ = shapeTree.AppendChild(BuildFilledRectangle(0, 0, SlideWidth, headerHeight, NavyDark));

            var logoDrawing = EmbedImageBytes(slidePart, logoBytes, 120000, 300000, 800000, 800000);
            if (logoDrawing is not null)
            {
                _ = shapeTree.AppendChild(logoDrawing);
            }

            _ = shapeTree.AppendChild(BuildTextBox(1150000, 280000, 6000000, 500000, "gamatrain", 3200, true, "FFFFFF"));
            _ = shapeTree.AppendChild(BuildTextBox(1150000, 800000, 6000000, 400000, "Learn Online, Test Online", 1400, false, TextTagline));

            if (!string.IsNullOrEmpty(exam?.QrCode))
            {
                var qrDrawing = await EmbedImageFromSourceAsync(slidePart, exam.QrCode, null, SlideWidth - 1000000, 300000, 800000, 800000);
                if (qrDrawing is not null)
                {
                    _ = shapeTree.AppendChild(qrDrawing);
                }
            }

            _ = shapeTree.AppendChild(BuildTextBox(Margin, headerHeight + 400000, SlideWidth - (2 * Margin), 900000,
                exam?.Title ?? string.Empty, 3600, true, NavyMid));

            _ = shapeTree.AppendChild(BuildTextBox(Margin, headerHeight + 1500000, SlideWidth - (2 * Margin), 500000,
                $"Questions: {exam?.TestsCount}      Time: {exam?.ExamTime} min", 1800, false, TextDark));

            var commonSlideData = new P.CommonSlideData();
            _ = commonSlideData.AppendChild(shapeTree);
            var slide = new P.Slide();
            _ = slide.AppendChild(commonSlideData);
            slidePart.Slide = slide;
            slidePart.Slide.Save();

            return slidePart;
        }

        // ---- Question slide ------------------------------------------------------------------------------

        private static async Task<SlidePart> BuildQuestionSlideAsync(
            PresentationPart presentationPart, SlideLayoutPart slideLayoutPart,
            ExamInformationResponseDto.TestDto test, int index, Lazy<HttpClient> httpClient)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            _ = slidePart.AddPart(slideLayoutPart);

            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);

            const long badgeSize = 700000;
            _ = shapeTree.AppendChild(BuildFilledRectangle(Margin, Margin, badgeSize, badgeSize, NavyBadge));
            _ = shapeTree.AppendChild(BuildTextBox(Margin, Margin, badgeSize, badgeSize, (index + 1).ToString(CultureInfo.InvariantCulture),
                2400, true, "FFFFFF", A.TextAlignmentTypeValues.Center, centerVertically: true));

            var questionX = Margin + badgeSize + 300000;
            var questionWidth = SlideWidth - questionX - Margin;
            _ = shapeTree.AppendChild(BuildRichTextBox(questionX, Margin, questionWidth, 1800000, test.Question, 2000, true, TextDark));

            var contentTop = Margin + 2000000;

            if (!string.IsNullOrEmpty(test.QuestionFile))
            {
                var imageDrawing = await EmbedImageFromSourceAsync(slidePart, test.QuestionFile, httpClient, questionX, contentTop, 2500000, 1600000);
                if (imageDrawing is not null)
                {
                    _ = shapeTree.AppendChild(imageDrawing);
                    contentTop += 1750000;
                }
            }

            if (test.HasOptions)
            {
                _ = shapeTree.AppendChild(BuildOptionsTable(test, questionX, contentTop, questionWidth));
            }

            var commonSlideData = new P.CommonSlideData();
            _ = commonSlideData.AppendChild(shapeTree);
            var slide = new P.Slide();
            _ = slide.AppendChild(commonSlideData);
            slidePart.Slide = slide;
            slidePart.Slide.Save();

            return slidePart;
        }

        private static P.GraphicFrame BuildOptionsTable(ExamInformationResponseDto.TestDto test, long x, long y, long width)
        {
            var badgeWidth = width / 10;
            var contentWidth = (width - (2 * badgeWidth)) / 2;

            var table = new A.Table();
            var tableProperties = new A.TableProperties { FirstRow = false, BandRow = false };
            _ = table.AppendChild(tableProperties);

            var grid = new A.TableGrid();
            _ = grid.AppendChild(new A.GridColumn { Width = badgeWidth });
            _ = grid.AppendChild(new A.GridColumn { Width = contentWidth });
            _ = grid.AppendChild(new A.GridColumn { Width = badgeWidth });
            _ = grid.AppendChild(new A.GridColumn { Width = contentWidth });
            _ = table.AppendChild(grid);

            const long rowHeight = 900000;
            _ = table.AppendChild(BuildOptionsTableRow("A", test.OptionA, "B", test.OptionB, rowHeight));
            _ = table.AppendChild(BuildOptionsTableRow("C", test.OptionC, "D", test.OptionD, rowHeight));

            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = NextShapeId(), Name = "OptionsTable" };
            var graphicFrameLocks = new A.GraphicFrameLocks { NoGrouping = true };
            var nonVisualGraphicFrameDrawingProperties = new P.NonVisualGraphicFrameDrawingProperties();
            _ = nonVisualGraphicFrameDrawingProperties.AppendChild(graphicFrameLocks);
            var nonVisualGraphicFrameProperties = new P.NonVisualGraphicFrameProperties();
            _ = nonVisualGraphicFrameProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualGraphicFrameProperties.AppendChild(nonVisualGraphicFrameDrawingProperties);
            _ = nonVisualGraphicFrameProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());

            var graphicFrame = new P.GraphicFrame();
            _ = graphicFrame.AppendChild(nonVisualGraphicFrameProperties);

            var transform = new P.Transform();
            _ = transform.AppendChild(new A.Offset { X = x, Y = y });
            _ = transform.AppendChild(new A.Extents { Cx = width, Cy = rowHeight * 2 });
            _ = graphicFrame.AppendChild(transform);

#pragma warning disable S1075 // spec-mandated DrawingML graphic-data URI, not a configurable endpoint
            var graphicData = new A.GraphicData { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" };
#pragma warning restore S1075
            _ = graphicData.AppendChild(table);
            var graphic = new A.Graphic();
            _ = graphic.AppendChild(graphicData);
            _ = graphicFrame.AppendChild(graphic);

            return graphicFrame;
        }

        private static A.TableRow BuildOptionsTableRow(string letterLeft, string? textLeft, string letterRight, string? textRight, long height)
        {
            var row = new A.TableRow { Height = height };
            _ = row.AppendChild(BuildOptionBadgeCell(letterLeft));
            _ = row.AppendChild(BuildOptionContentCell(textLeft));
            _ = row.AppendChild(BuildOptionBadgeCell(letterRight));
            _ = row.AppendChild(BuildOptionContentCell(textRight));
            return row;
        }

        private static A.TableCell BuildOptionBadgeCell(string letter)
        {
            var cell = new A.TableCell();
            var textBody = new A.TextBody();
            _ = textBody.AppendChild(new A.BodyProperties());
            _ = textBody.AppendChild(new A.ListStyle());
            var paragraph = new A.Paragraph();
            _ = paragraph.AppendChild(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center });
            _ = paragraph.AppendChild(BuildDrawingRun(letter, 1600, true, "FFFFFF"));
            _ = textBody.AppendChild(paragraph);
            _ = cell.AppendChild(textBody);

            var cellProperties = new A.TableCellProperties { Anchor = A.TextAnchoringTypeValues.Center };
            var badgeFill = new A.SolidFill();
            _ = badgeFill.AppendChild(new A.RgbColorModelHex { Val = NavyMid });
            _ = cellProperties.AppendChild(badgeFill);
            _ = cell.AppendChild(cellProperties);
            return cell;
        }

        private static A.TableCell BuildOptionContentCell(string? optionText)
        {
            var cell = new A.TableCell();
            var textBody = new A.TextBody();
            _ = textBody.AppendChild(new A.BodyProperties());
            _ = textBody.AppendChild(new A.ListStyle());
            foreach (var paragraph in BuildRichParagraphs(optionText, 1400, false, TextDark))
            {
                _ = textBody.AppendChild(paragraph);
            }

            _ = cell.AppendChild(textBody);

            var cellProperties = new A.TableCellProperties { Anchor = A.TextAnchoringTypeValues.Center };
            var contentFill = new A.SolidFill();
            _ = contentFill.AppendChild(new A.RgbColorModelHex { Val = "FFFFFF" });
            _ = cellProperties.AppendChild(contentFill);
            _ = cell.AppendChild(cellProperties);
            return cell;
        }

        // ---- Shape helpers -------------------------------------------------------------------------------

        /// <summary>
        /// Appends the two elements every p:spTree must start with: p:nvGrpSpPr then p:grpSpPr, in that
        /// order (CT_GroupShape). Both are structurally required even though this builder never nests or
        /// groups shapes -- omitting nvGrpSpPr entirely (an earlier revision did) fails schema validation.
        /// </summary>
        private static void AppendGroupShapeProperties(P.ShapeTree shapeTree)
        {
            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty };
            var nonVisualGroupShapeProperties = new P.NonVisualGroupShapeProperties();
            _ = nonVisualGroupShapeProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualGroupShapeProperties.AppendChild(new P.NonVisualGroupShapeDrawingProperties());
            _ = nonVisualGroupShapeProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());
            _ = shapeTree.AppendChild(nonVisualGroupShapeProperties);

            var properties = new P.GroupShapeProperties();
            var transform = new A.TransformGroup();
            _ = transform.AppendChild(new A.Offset { X = 0, Y = 0 });
            _ = transform.AppendChild(new A.Extents { Cx = 0, Cy = 0 });
            _ = transform.AppendChild(new A.ChildOffset { X = 0, Y = 0 });
            _ = transform.AppendChild(new A.ChildExtents { Cx = 0, Cy = 0 });
            _ = properties.AppendChild(transform);
            _ = shapeTree.AppendChild(properties);
        }

        private static P.Shape BuildFilledRectangle(long x, long y, long cx, long cy, string fillHex)
        {
            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = NextShapeId(), Name = "Rectangle" };
            var nonVisualShapeProperties = new P.NonVisualShapeProperties();
            _ = nonVisualShapeProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualShapeProperties.AppendChild(new P.NonVisualShapeDrawingProperties());
            _ = nonVisualShapeProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());

            var shape = new P.Shape();
            _ = shape.AppendChild(nonVisualShapeProperties);

            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(new A.Offset { X = x, Y = y });
            _ = transform2D.AppendChild(new A.Extents { Cx = cx, Cy = cy });

            var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
            _ = presetGeometry.AppendChild(new A.AdjustValueList());

            var fill = new A.SolidFill();
            _ = fill.AppendChild(new A.RgbColorModelHex { Val = fillHex });

            var outline = new A.Outline();
            _ = outline.AppendChild(new A.NoFill());

            var shapeProperties = new P.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(presetGeometry);
            _ = shapeProperties.AppendChild(fill);
            _ = shapeProperties.AppendChild(outline);
            _ = shape.AppendChild(shapeProperties);

            return shape;
        }

        private static P.Shape BuildTextBox(long x, long y, long cx, long cy, string text, int fontSizeHundredths,
            bool bold, string colorHex, A.TextAlignmentTypeValues? align = null, bool centerVertically = false)
            => BuildTextBoxCore(x, y, cx, cy, [BuildTextParagraph(text, fontSizeHundredths, bold, colorHex, align)], centerVertically);

        /// <summary>Like <see cref="BuildTextBox"/>, but <paramref name="html"/> is rich content (may contain
        /// a <c>data-omml-b64</c> formula marker from <c>RenderFormulasToOmmlAsync</c>) rather than a plain
        /// string -- see <see cref="BuildRichParagraphs"/>.</summary>
        private static P.Shape BuildRichTextBox(long x, long y, long cx, long cy, string? html, int fontSizeHundredths,
            bool bold, string colorHex, A.TextAlignmentTypeValues? align = null, bool centerVertically = false)
            => BuildTextBoxCore(x, y, cx, cy, BuildRichParagraphs(html, fontSizeHundredths, bold, colorHex, align), centerVertically);

        private static P.Shape BuildTextBoxCore(long x, long y, long cx, long cy, List<A.Paragraph> paragraphs, bool centerVertically)
        {
            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = NextShapeId(), Name = "TextBox" };
            var nonVisualShapeProperties = new P.NonVisualShapeProperties();
            _ = nonVisualShapeProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualShapeProperties.AppendChild(new P.NonVisualShapeDrawingProperties { TextBox = true });
            _ = nonVisualShapeProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());

            var shape = new P.Shape();
            _ = shape.AppendChild(nonVisualShapeProperties);

            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(new A.Offset { X = x, Y = y });
            _ = transform2D.AppendChild(new A.Extents { Cx = cx, Cy = cy });

            var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
            _ = presetGeometry.AppendChild(new A.AdjustValueList());

            var shapeProperties = new P.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(presetGeometry);
            _ = shapeProperties.AppendChild(new A.NoFill());
            _ = shape.AppendChild(shapeProperties);

            // p:sp requires PresentationML's own p:txBody wrapper here, not DrawingML's a:txBody (used
            // inside DrawingML table cells elsewhere in this file) -- Word/PowerPoint's schema validator
            // rejects the wrong one even though the a:bodyPr/a:lstStyle/a:p children inside are identical.
            var textBody = new P.TextBody();
            var bodyProperties = new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = centerVertically ? A.TextAnchoringTypeValues.Center : A.TextAnchoringTypeValues.Top };
            _ = textBody.AppendChild(bodyProperties);
            _ = textBody.AppendChild(new A.ListStyle());
            foreach (var paragraph in paragraphs)
            {
                _ = textBody.AppendChild(paragraph);
            }

            _ = shape.AppendChild(textBody);

            return shape;
        }

        private static A.Run BuildDrawingRun(string text, int fontSizeHundredths, bool bold, string colorHex)
        {
            var run = new A.Run();
            var runProperties = new A.RunProperties { Language = "en-US", FontSize = fontSizeHundredths, Bold = bold };
            var fill = new A.SolidFill();
            _ = fill.AppendChild(new A.RgbColorModelHex { Val = colorHex });
            _ = runProperties.AppendChild(fill);
            _ = run.AppendChild(runProperties);
            _ = run.AppendChild(new A.Text(text));
            return run;
        }

        private static A.Paragraph BuildTextParagraph(string text, int fontSizeHundredths, bool bold, string colorHex, A.TextAlignmentTypeValues? align)
        {
            var paragraph = new A.Paragraph();
            if (align is not null)
            {
                _ = paragraph.AppendChild(new A.ParagraphProperties { Alignment = align });
            }

            _ = paragraph.AppendChild(BuildDrawingRun(text, fontSizeHundredths, bold, colorHex));
            return paragraph;
        }

        /// <summary>
        /// Parses a question/option HTML fragment into DrawingML paragraphs: consecutive text becomes one
        /// paragraph (as before, when nothing here contains a formula), and each <c>data-omml-b64</c>
        /// marker (from <c>HeadlessBrowserRenderProvider.RenderFormulasToOmmlAsync</c>) becomes its own
        /// dedicated equation paragraph via <see cref="BuildEquationParagraph"/> -- unlike
        /// <see cref="ExamWordRichText"/>, a formula can't stay inline mid-run here (PowerPoint's
        /// mc:AlternateContent/a14:m equation wrapper isn't valid as one of several children mixed into a
        /// single a:p alongside plain a:r runs), so it surfaces as its own line instead. Still a real
        /// improvement over losing the formula entirely, which is what the plain-text-only path used to do.
        /// Rich formatting (bold/italic/color) inside the fragment is still not preserved -- same known gap
        /// as before, out of scope here.
        /// </summary>
        private static List<A.Paragraph> BuildRichParagraphs(string? html, int fontSizeHundredths, bool bold, string colorHex, A.TextAlignmentTypeValues? align = null)
        {
            var paragraphs = new List<A.Paragraph>();
            if (string.IsNullOrWhiteSpace(html))
            {
                paragraphs.Add(BuildTextParagraph(string.Empty, fontSizeHundredths, bold, colorHex, align));
                return paragraphs;
            }

            var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
            var textBuffer = new StringBuilder();

            void FlushText()
            {
                var text = textBuffer.ToString().Trim();
                _ = textBuffer.Clear();
                if (!string.IsNullOrEmpty(text))
                {
                    paragraphs.Add(BuildTextParagraph(text, fontSizeHundredths, bold, colorHex, align));
                }
            }

            void Walk(INode node)
            {
                if (node.NodeType == NodeType.Text)
                {
                    _ = textBuffer.Append(node.TextContent);
                    return;
                }

                if (node is not IElement element)
                {
                    return;
                }

                if (element.NodeName == "SPAN" && element.HasAttribute("data-omml-b64"))
                {
                    FlushText();
                    var equationParagraph = BuildEquationParagraph(element.GetAttribute("data-omml-b64"));
                    if (equationParagraph is not null)
                    {
                        paragraphs.Add(equationParagraph);
                    }

                    return;
                }

                foreach (var child in element.ChildNodes)
                {
                    Walk(child);
                }
            }

            foreach (var child in document.Body!.ChildNodes)
            {
                Walk(child);
            }

            FlushText();

            if (paragraphs.Count == 0)
            {
                paragraphs.Add(BuildTextParagraph(string.Empty, fontSizeHundredths, bold, colorHex, align));
            }

            return paragraphs;
        }

        /// <summary>
        /// Wraps an already-complete <c>&lt;m:oMath&gt;</c> fragment (base64-decoded) in the
        /// <c>mc:AlternateContent</c>/<c>a14:m</c> structure PowerPoint requires for an equation inside a
        /// slide's text body -- unlike WordprocessingML, DrawingML's <c>a:p</c> schema has no direct slot
        /// for <c>m:oMath</c>; PowerPoint 2010+ represents equations via this markup-compatibility
        /// extension instead. <c>mc:Fallback</c> is a plain placeholder run for older consumers that don't
        /// recognize the "a14" extension, so they see a marker rather than a silently empty paragraph.
        /// Returns <see langword="null"/> on any malformed input -- one bad formula drops silently rather
        /// than risking a corrupt slide.
        /// </summary>
        private static A.Paragraph? BuildEquationParagraph(string? ommlBase64)
        {
            if (string.IsNullOrEmpty(ommlBase64))
            {
                return null;
            }

            try
            {
                var ommlXml = Encoding.UTF8.GetString(Convert.FromBase64String(ommlBase64));
                var paragraphXml = $"""
                    <a:p xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:a14="http://schemas.microsoft.com/office/drawing/2010/main">
                      <mc:AlternateContent>
                        <mc:Choice Requires="a14">
                          <a14:m>
                            <m:oMathPara xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
                              {ommlXml}
                            </m:oMathPara>
                          </a14:m>
                        </mc:Choice>
                        <mc:Fallback>
                          <a:r><a:rPr lang="en-US" /><a:t>[equation]</a:t></a:r>
                        </mc:Fallback>
                      </mc:AlternateContent>
                    </a:p>
                    """;
                return new A.Paragraph(paragraphXml);
            }
            catch (Exception exc) when (exc is FormatException or System.Xml.XmlException)
            {
                return null;
            }
        }

        private static uint NextShapeId() => Interlocked.Increment(ref shapeIdCounter);

        // ---- Images ------------------------------------------------------------------------------------

        private static async Task<P.Picture?> EmbedImageFromSourceAsync(
            SlidePart slidePart, string src, Lazy<HttpClient>? httpClient, long x, long y, long cx, long cy)
        {
            byte[] bytes;
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',', StringComparison.Ordinal);
                if (comma < 0)
                {
                    return null;
                }

                bytes = Convert.FromBase64String(src[(comma + 1)..]);
            }
            else if (httpClient is not null && Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
            {
                try
                {
                    bytes = await httpClient.Value.GetByteArrayAsync(absoluteUri);
                }
                catch (HttpRequestException)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            return EmbedImageBytes(slidePart, bytes, x, y, cx, cy);
        }

        private static P.Picture? EmbedImageBytes(SlidePart slidePart, byte[] bytes, long x, long y, long cx, long cy)
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                return null;
            }

            var widthPx = (int)Math.Round(cx / (double)EmuPerPixel);
            var heightPx = (int)Math.Round(cy / (double)EmuPerPixel);
            using var resizedBitmap = widthPx != bitmap.Width || heightPx != bitmap.Height
                ? bitmap.Resize(new SKImageInfo(Math.Max(1, widthPx), Math.Max(1, heightPx)), SKSamplingOptions.Default)
                : null;
            var encodeSource = resizedBitmap ?? bitmap;

            var imagePart = slidePart.AddImagePart(ImagePartType.Png);
            using (var pngStream = new MemoryStream())
            {
                using (var image = SKImage.FromBitmap(encodeSource))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                {
                    data.SaveTo(pngStream);
                }

                pngStream.Position = 0;
                imagePart.FeedData(pngStream);
            }

            var relationshipId = slidePart.GetIdOfPart(imagePart);
            var drawingId = NextShapeId();

            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = drawingId, Name = $"image{drawingId}.png" };
            var pictureLocks = new A.PictureLocks { NoChangeAspect = true };
            var nonVisualPictureDrawingProperties = new P.NonVisualPictureDrawingProperties();
            _ = nonVisualPictureDrawingProperties.AppendChild(pictureLocks);
            var nonVisualPictureProperties = new P.NonVisualPictureProperties();
            _ = nonVisualPictureProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualPictureProperties.AppendChild(nonVisualPictureDrawingProperties);
            _ = nonVisualPictureProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());

            var picture = new P.Picture();
            _ = picture.AppendChild(nonVisualPictureProperties);

            var stretch = new A.Stretch();
            _ = stretch.AppendChild(new A.FillRectangle());
            var blipFill = new P.BlipFill();
            _ = blipFill.AppendChild(new A.Blip { Embed = relationshipId });
            _ = blipFill.AppendChild(stretch);
            _ = picture.AppendChild(blipFill);

            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(new A.Offset { X = x, Y = y });
            _ = transform2D.AppendChild(new A.Extents { Cx = cx, Cy = cy });
            var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
            _ = presetGeometry.AppendChild(new A.AdjustValueList());

            var shapeProperties = new P.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(presetGeometry);
            _ = picture.AppendChild(shapeProperties);

            return picture;
        }

        // ---- Minimal required parts: theme / slide master / slide layout ------------------------------

        private static ThemePart BuildThemePart(PresentationPart presentationPart)
        {
            var themePart = presentationPart.AddNewPart<ThemePart>();
            var theme = new A.Theme { Name = "GamaEdtechExamTheme" };
            var themeElements = new A.ThemeElements();

            var colorScheme = new A.ColorScheme { Name = "GamaEdtech" };
            _ = colorScheme.AppendChild(BuildThemeColor<A.Dark1Color>(new A.SystemColor { Val = A.SystemColorValues.WindowText }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Light1Color>(new A.SystemColor { Val = A.SystemColorValues.Window }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Dark2Color>(new A.RgbColorModelHex { Val = NavyDark }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Light2Color>(new A.RgbColorModelHex { Val = RowGrayBg }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent1Color>(new A.RgbColorModelHex { Val = NavyMid }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent2Color>(new A.RgbColorModelHex { Val = Yellow }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent3Color>(new A.RgbColorModelHex { Val = BorderGray }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent4Color>(new A.RgbColorModelHex { Val = TextMuted }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent5Color>(new A.RgbColorModelHex { Val = TextDark }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent6Color>(new A.RgbColorModelHex { Val = NavyBadge }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Hyperlink>(new A.RgbColorModelHex { Val = NavyMid }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.FollowedHyperlinkColor>(new A.RgbColorModelHex { Val = NavyDark }));
            _ = themeElements.AppendChild(colorScheme);

            var fontScheme = new A.FontScheme { Name = "GamaEdtech" };
            var majorFont = new A.MajorFont();
            _ = majorFont.AppendChild(new A.LatinFont { Typeface = "Arial" });
            _ = majorFont.AppendChild(new A.EastAsianFont { Typeface = string.Empty });
            _ = majorFont.AppendChild(new A.ComplexScriptFont { Typeface = string.Empty });
            var minorFont = new A.MinorFont();
            _ = minorFont.AppendChild(new A.LatinFont { Typeface = "Arial" });
            _ = minorFont.AppendChild(new A.EastAsianFont { Typeface = string.Empty });
            _ = minorFont.AppendChild(new A.ComplexScriptFont { Typeface = string.Empty });
            _ = fontScheme.AppendChild(majorFont);
            _ = fontScheme.AppendChild(minorFont);
            _ = themeElements.AppendChild(fontScheme);

            _ = themeElements.AppendChild(BuildMinimalFormatScheme());
            _ = theme.AppendChild(themeElements);
            _ = theme.AppendChild(new A.ObjectDefaults());
            themePart.Theme = theme;
            themePart.Theme.Save();
            return themePart;
        }

        private static T BuildThemeColor<T>(OpenXmlElement colorValue)
            where T : OpenXmlCompositeElement, new()
        {
            var element = new T();
            _ = element.AppendChild(colorValue);
            return element;
        }

        private static A.FormatScheme BuildMinimalFormatScheme()
        {
            var formatScheme = new A.FormatScheme { Name = "GamaEdtech" };

            var fillStyleList = new A.FillStyleList();
            _ = fillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = fillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = fillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = formatScheme.AppendChild(fillStyleList);

            var lineStyleList = new A.LineStyleList();
            for (var i = 0; i < 3; i++)
            {
                var outline = new A.Outline { Width = 6350 };
                _ = outline.AppendChild(BuildPlaceholderColorFill());
                _ = lineStyleList.AppendChild(outline);
            }

            _ = formatScheme.AppendChild(lineStyleList);

            var effectStyleList = new A.EffectStyleList();
            for (var i = 0; i < 3; i++)
            {
                var effectStyle = new A.EffectStyle();
                _ = effectStyle.AppendChild(new A.EffectList());
                _ = effectStyleList.AppendChild(effectStyle);
            }

            _ = formatScheme.AppendChild(effectStyleList);

            var backgroundFillStyleList = new A.BackgroundFillStyleList();
            _ = backgroundFillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = backgroundFillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = backgroundFillStyleList.AppendChild(BuildPlaceholderColorFill());
            _ = formatScheme.AppendChild(backgroundFillStyleList);

            return formatScheme;
        }

        private static A.SolidFill BuildPlaceholderColorFill()
        {
            var fill = new A.SolidFill();
            _ = fill.AppendChild(new A.SchemeColor { Val = A.SchemeColorValues.PhColor });
            return fill;
        }

        private static SlideMasterPart BuildSlideMasterPart(PresentationPart presentationPart, ThemePart themePart)
        {
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
            _ = slideMasterPart.AddPart(themePart);

            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);

            var commonSlideData = new P.CommonSlideData();
            _ = commonSlideData.AppendChild(shapeTree);

            var slideMaster = new P.SlideMaster();
            _ = slideMaster.AppendChild(commonSlideData);
            _ = slideMaster.AppendChild(new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
            });

            slideMasterPart.SlideMaster = slideMaster;
            slideMasterPart.SlideMaster.Save();
            return slideMasterPart;
        }

        private static SlideLayoutPart BuildSlideLayoutPart(SlideMasterPart slideMasterPart)
        {
            // No direct theme relationship here: a SlideLayoutPart inherits its theme through the
            // master->layout chain already established by AddNewPart<SlideLayoutPart>() below.
            // Attempting AddPart(slideMasterPart.ThemePart) throws -- the SDK doesn't allow adding a
            // part that already belongs to a different parent's relationship graph a second time.
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();

            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);

            var commonSlideData = new P.CommonSlideData();
            _ = commonSlideData.AppendChild(shapeTree);

            var slideLayout = new P.SlideLayout { Type = P.SlideLayoutValues.Blank, Preserve = true };
            _ = slideLayout.AppendChild(commonSlideData);
            var colorMapOverride = new P.ColorMapOverride();
            _ = colorMapOverride.AppendChild(new A.MasterColorMapping());
            _ = slideLayout.AppendChild(colorMapOverride);

            slideLayoutPart.SlideLayout = slideLayout;
            slideLayoutPart.SlideLayout.Save();

            var slideLayoutIdList = new P.SlideLayoutIdList();
            _ = slideLayoutIdList.AppendChild(new P.SlideLayoutId { Id = 2147483649, RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart) });
            _ = slideMasterPart.SlideMaster!.AppendChild(slideLayoutIdList);
            slideMasterPart.SlideMaster.Save();

            return slideLayoutPart;
        }
    }
}
