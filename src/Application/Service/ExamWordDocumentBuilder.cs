namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Data.Dto.Game;

    using SkiaSharp;

    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using Vml = DocumentFormat.OpenXml.Vml;
    using A = DocumentFormat.OpenXml.Drawing;
    using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
    using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

    /// <summary>
    /// Builds an exam Word document entirely via native OOXML -- no HtmlToOpenXml conversion layer.
    /// Exists because that layer couldn't produce genuinely native-quality Word tables (silently applied
    /// its own default table style, mishandled bare-pixel widths, etc. -- see
    /// docs/business/exams-and-content.md); every visual property here is set directly, matching
    /// exam.word.html's design (same colors/layout) without going through HTML/CSS at all.
    /// </summary>
    internal static class ExamWordDocumentBuilder
    {
        private const long EmuPerPixel = 9525; // 96dpi CSS px -> EMU
        private const int MaxImageWidthPx = 500;
        private const string NavyDark = "172437";
        private const string NavyMid = "21324A";
        private const string NavyBadge = "202C3E";
        private const string Yellow = "F6B500";
        private const string BorderGray = "9AABBA";
        private const string BorderLightGray = "C5D0DB";
        private const string RowGrayBg = "F4F7FB";
        private const string TextDark = "172033";
        private const string TextMuted = "5B6777";
        private const string TextTagline = "D5DDE8";

        // Only needs document-local uniqueness (OOXML drawing IDs aren't referenced across files), so a
        // simple incrementing counter is enough -- Random would be gratuitous and trips CA5394.
        private static long imageIdCounter;

        public static async Task<byte[]> BuildAsync(
            [NotNull] ExamInformationResponseDto data, byte[] logoBytes, string? watermarkText, Lazy<HttpClient> httpClient)
        {
            using MemoryStream stream = new();
            using (var wordDocument = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = wordDocument.AddMainDocumentPart();
                var body = new Ooxml.Body();
                var document = new Ooxml.Document();
                _ = document.AppendChild(body);
                mainPart.Document = document;

                _ = body.AppendChild(await BuildHeaderRowAsync(data.Exam, mainPart, logoBytes));
                _ = body.AppendChild(new Ooxml.Paragraph());
                _ = body.AppendChild(BuildTitleBar(data.Exam?.Title));
                _ = body.AppendChild(new Ooxml.Paragraph());
                _ = body.AppendChild(BuildMetadataTable(data.Exam));

                if (data.Tests is not null)
                {
                    for (var i = 0; i < data.Tests.Count; i++)
                    {
                        _ = body.AppendChild(new Ooxml.Paragraph());
                        _ = body.AppendChild(await BuildQuestionTableAsync(data.Tests[i], i, mainPart, httpClient));
                    }
                }

                RemoveDefaultTableStyle(body);
                PreventRowsSplittingAcrossPages(body);

                _ = body.AppendChild(new Ooxml.SectionProperties());
                var sectionProperties = body.Elements<Ooxml.SectionProperties>().Single();
                _ = sectionProperties.AppendChild(new Ooxml.PageSize { Width = 11906, Height = 16838 });
                _ = sectionProperties.AppendChild(new Ooxml.PageMargin
                {
                    Top = 1440,
                    Right = 1440,
                    Bottom = 1440,
                    Left = 1440,
                    Header = 720,
                    Footer = 720,
                    Gutter = 0,
                });

                AddPageHeaderAndFooter(mainPart, data.Exam?.Title, watermarkText);

                mainPart.Document.Save();
            }

            return stream.ToArray();
        }

        // ---- Header / title / metadata --------------------------------------------------------------

        private static async Task<Ooxml.Table> BuildHeaderRowAsync(
            ExamInformationResponseDto.ExamDto? exam, MainDocumentPart mainPart, byte[] logoBytes)
        {
            var table = new Ooxml.Table();
            _ = table.AppendChild(ShadedTableProperties(NavyDark));
            AppendTableGrid(table, 812, 7402, 812);

            var row = new Ooxml.TableRow();

            var logoDrawing = EmbedImageBytes(mainPart, logoBytes, 52, 52);
            var logoRun = new Ooxml.Run();
            if (logoDrawing is not null)
            {
                _ = logoRun.AppendChild(logoDrawing);
            }

            _ = row.AppendChild(ShadedCell(NavyDark, WrapInParagraph(logoRun), Ooxml.JustificationValues.Left, widthPct: "450"));

            var brandParagraph1 = new Ooxml.Paragraph();
            _ = brandParagraph1.AppendChild(CreateRun("gamatrain", bold: true, colorHex: "FFFFFF", fontSizeHalfPoints: 34));
            var brandParagraph2 = new Ooxml.Paragraph();
            _ = brandParagraph2.AppendChild(CreateRun("Learn Online, Test Online", bold: false, colorHex: TextTagline, fontSizeHalfPoints: 18));
            var brandCell = new Ooxml.TableCell();
            var brandCellProperties = new Ooxml.TableCellProperties();
            _ = brandCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "4100", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = brandCellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = NavyDark });
            _ = brandCell.AppendChild(brandCellProperties);
            _ = brandCell.AppendChild(brandParagraph1);
            _ = brandCell.AppendChild(brandParagraph2);
            _ = row.AppendChild(brandCell);

            var qrRun = new Ooxml.Run();
            if (!string.IsNullOrEmpty(exam?.QrCode))
            {
                var qrDrawing = await EmbedImageFromSourceAsync(mainPart, exam.QrCode, null, 58, 58);
                if (qrDrawing is not null)
                {
                    _ = qrRun.AppendChild(qrDrawing);
                }
            }

            _ = row.AppendChild(ShadedCell(NavyDark, WrapInParagraph(qrRun), Ooxml.JustificationValues.Right, widthPct: "450"));

            _ = table.AppendChild(row);
            return table;
        }

        private static Ooxml.Table BuildTitleBar(string? title)
        {
            var table = new Ooxml.Table();
            _ = table.AppendChild(ShadedTableProperties(NavyMid));
            AppendTableGrid(table, 9026);

            var row = new Ooxml.TableRow();
            var paragraph = new Ooxml.Paragraph();
            _ = paragraph.AppendChild(CreateRun(title ?? string.Empty, bold: true, colorHex: "FFFFFF", fontSizeHalfPoints: 40));
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = NavyMid });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            _ = row.AppendChild(cell);
            _ = table.AppendChild(row);
            return table;
        }

        private static Ooxml.Table BuildMetadataTable(ExamInformationResponseDto.ExamDto? exam)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = tableProperties.AppendChild(BorderedTableBorders(BorderLightGray));
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 3069, 1895, 1986, 2076);

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BorderedCell("Name: ________________________", BorderLightGray, "1700"));
            _ = row.AppendChild(BorderedCell("Date: ____________", BorderLightGray, "1050"));
            _ = row.AppendChild(BorderedCell($"Questions: {exam?.TestsCount}", BorderLightGray, "1100"));
            _ = row.AppendChild(BorderedCell($"Time: {exam?.ExamTime} min", BorderLightGray, "1150"));
            _ = table.AppendChild(row);
            return table;
        }

        // ---- Per-question ------------------------------------------------------------------------------

        /// <summary>
        /// Each question is its own top-level table (a direct child of the body), immediately followed by
        /// its own separate options table when it has options -- verified against a genuine Word document
        /// (Word's own PDF-reconstruction of an earlier export) that every visually distinct table is a
        /// sibling in the body, never nested inside another table's cell. Nesting one inside a question's
        /// content cell (an earlier revision of this code did) produces a table Word treats as fundamentally
        /// different/less reliable for width and page-break behavior than a flat, sibling table.
        /// </summary>
        private static async Task<Ooxml.Table> BuildQuestionTableAsync(
            ExamInformationResponseDto.TestDto test, int index, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            var borders = new Ooxml.TableBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = BorderGray, Size = 8 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = BorderGray, Size = 8 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = Yellow, Size = 24 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = BorderGray, Size = 8 });
            _ = tableProperties.AppendChild(borders);
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 722, 8304);

            var rowBg = index % 2 == 0 ? "FFFFFF" : RowGrayBg;

            var row = new Ooxml.TableRow();

            var numberParagraph = new Ooxml.Paragraph();
            _ = numberParagraph.AppendChild(CreateRun($" {index + 1} ", bold: true, colorHex: "FFFFFF", fontSizeHalfPoints: 22, shadeHex: NavyBadge));
            var numberCell = new Ooxml.TableCell();
            _ = numberCell.AppendChild(new Ooxml.TableCellProperties(
                new Ooxml.TableCellWidth { Width = "400", Type = Ooxml.TableWidthUnitValues.Pct },
                new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = RowGrayBg },
                new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Top }));
            _ = numberCell.AppendChild(numberParagraph);
            _ = row.AppendChild(numberCell);

            var contentCell = new Ooxml.TableCell();
            var contentCellProperties = new Ooxml.TableCellProperties();
            _ = contentCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "4600", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = contentCellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = rowBg });
            _ = contentCell.AppendChild(contentCellProperties);

            var questionFormat = new ExamWordRichText.RunFormat(true, false, false, false, false, TextDark, 22);
            var questionParagraphs = await ExamWordRichText.ParseToParagraphsAsync(test.Question, mainPart, httpClient, questionFormat);
            foreach (var paragraph in questionParagraphs)
            {
                _ = contentCell.AppendChild(paragraph);
            }

            if (!string.IsNullOrEmpty(test.QuestionFile))
            {
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, test.QuestionFile, httpClient, null, null);
                if (imageDrawing is not null)
                {
                    var imageParagraph = new Ooxml.Paragraph();
                    var imageRun = new Ooxml.Run();
                    _ = imageRun.AppendChild(imageDrawing);
                    _ = imageParagraph.AppendChild(imageRun);
                    _ = contentCell.AppendChild(imageParagraph);
                }
            }

            if (test.HasOptions)
            {
                _ = contentCell.AppendChild(await BuildOptionsGridAsync(test, mainPart, httpClient));

                // A table cell's content must end with a paragraph, not a table -- a cell whose last
                // child is a nested w:tbl (as this one was, immediately closed by </w:tc>) is exactly
                // what caused Word to render the options grid as if it had broken out of the cell.
                _ = contentCell.AppendChild(new Ooxml.Paragraph());
            }

            _ = row.AppendChild(contentCell);
            _ = table.AppendChild(row);
            return table;
        }

        /// <summary>
        /// Each option is TWO cells -- a narrow shaded badge cell (letter) plus its own content cell --
        /// not one cell with a colored run and a tab character faking a badge.
        /// </summary>
        private static async Task<Ooxml.Table> BuildOptionsGridAsync(
            ExamInformationResponseDto.TestDto test, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            // A Pct-width nested table resolves against the wrong base in Word often enough to be
            // unusable here -- it rendered wide enough to spill out from under the description cell
            // and appear as extra full-width rows under the whole question table. Absolute dxa sizing,
            // safely smaller than the ~8304dxa content cell (minus its default ~216dxa margins), avoids
            // that ambiguity entirely: this table's declared width is never larger than its container.
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "7800", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = tableProperties.AppendChild(NoTableBorders());
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 450, 3450, 450, 3450);

            var rowAB = new Ooxml.TableRow();
            _ = rowAB.AppendChild(BuildOptionBadgeCell("A"));
            _ = rowAB.AppendChild(await BuildOptionContentCellAsync(test.OptionA, test.OptionAFile, mainPart, httpClient));
            _ = rowAB.AppendChild(BuildOptionBadgeCell("B"));
            _ = rowAB.AppendChild(await BuildOptionContentCellAsync(test.OptionB, test.OptionBFile, mainPart, httpClient));
            _ = table.AppendChild(rowAB);

            var rowCD = new Ooxml.TableRow();
            _ = rowCD.AppendChild(BuildOptionBadgeCell("C"));
            _ = rowCD.AppendChild(await BuildOptionContentCellAsync(test.OptionC, test.OptionCFile, mainPart, httpClient));
            _ = rowCD.AppendChild(BuildOptionBadgeCell("D"));
            _ = rowCD.AppendChild(await BuildOptionContentCellAsync(test.OptionD, test.OptionDFile, mainPart, httpClient));
            _ = table.AppendChild(rowCD);

            return table;
        }

        private static Ooxml.TableCell BuildOptionBadgeCell(string letter)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(CreateRun(letter, bold: true, colorHex: "FFFFFF", fontSizeHalfPoints: 18));

            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "450", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = NavyMid });
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static async Task<Ooxml.TableCell> BuildOptionContentCellAsync(
            string? optionHtml, string? optionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "3450", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);

            var format = new ExamWordRichText.RunFormat(false, false, false, false, false, TextDark, 20);
            var paragraphs = await ExamWordRichText.ParseToParagraphsAsync(optionHtml, mainPart, httpClient, format);
            foreach (var paragraph in paragraphs)
            {
                _ = cell.AppendChild(paragraph);
            }

            if (!string.IsNullOrEmpty(optionFile))
            {
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, optionFile, httpClient, null, null);
                if (imageDrawing is not null)
                {
                    var imageParagraph = new Ooxml.Paragraph();
                    var imageRun = new Ooxml.Run();
                    _ = imageRun.AppendChild(imageDrawing);
                    _ = imageParagraph.AppendChild(imageRun);
                    _ = cell.AppendChild(imageParagraph);
                }
            }

            return cell;
        }

        // ---- Small shared builders ------------------------------------------------------------------

        private static Ooxml.TableProperties ShadedTableProperties(string fillHex)
        {
            var properties = new Ooxml.TableProperties();
            _ = properties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = properties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = fillHex });
            _ = properties.AppendChild(FixedTableLayout());
            return properties;
        }

        /// <summary>
        /// Appends a <c>w:tblGrid</c> with one column per width in <paramref name="columnWidthsDxa"/> --
        /// required immediately after <c>w:tblPr</c> on every table by the OOXML schema (CT_Tbl); Word
        /// treats a table missing it as invalid and silently repairs the document on open. The dxa values
        /// are also what a <c>Fixed</c>-layout table actually uses to size columns; without them Word's
        /// default autofit sizes every column from its content instead of the intended proportions, which
        /// is why the number/content/option columns weren't lining up.
        /// </summary>
        private static void AppendTableGrid(Ooxml.Table table, params int[] columnWidthsDxa)
        {
            var grid = new Ooxml.TableGrid();
            foreach (var width in columnWidthsDxa)
            {
                _ = grid.AppendChild(new Ooxml.GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) });
            }

            _ = table.AppendChild(grid);
        }

        /// <summary>Forces Word to honor the declared column widths (tblGrid/tcW) instead of resizing columns to fit their content.</summary>
        private static Ooxml.TableLayout FixedTableLayout() => new() { Type = Ooxml.TableLayoutValues.Fixed };

        /// <summary>Explicit zero-width "no border" on all four sides -- distinct from omitting tcBorders entirely, which leaves the cell's border unspecified rather than guaranteed-off.</summary>
        private static Ooxml.TableCellBorders NoTableCellBorders()
        {
            var borders = new Ooxml.TableCellBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            return borders;
        }

        /// <summary>Table-level equivalent of <see cref="NoTableCellBorders"/> -- explicit "none" on all outer/inside sides, rather than leaving the whole table's border unspecified.</summary>
        private static Ooxml.TableBorders NoTableBorders()
        {
            var borders = new Ooxml.TableBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.InsideHorizontalBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.InsideVerticalBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            return borders;
        }

        private static Ooxml.TableCell ShadedCell(string fillHex, Ooxml.Paragraph paragraph, Ooxml.JustificationValues alignment, string? widthPct = null)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            if (widthPct is not null)
            {
                _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthPct, Type = Ooxml.TableWidthUnitValues.Pct });
            }

            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = fillHex });
            _ = cell.AppendChild(cellProperties);
            var properties = new Ooxml.ParagraphProperties();
            _ = properties.AppendChild(new Ooxml.Justification { Val = alignment });
            _ = paragraph.InsertAt(properties, 0);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static Ooxml.Paragraph WrapInParagraph(Ooxml.Run run)
        {
            var paragraph = new Ooxml.Paragraph();
            _ = paragraph.AppendChild(run);
            return paragraph;
        }

        private static Ooxml.TableBorders BorderedTableBorders(string colorHex)
        {
            var borders = new Ooxml.TableBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = colorHex, Size = 4 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = colorHex, Size = 4 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = colorHex, Size = 4 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = colorHex, Size = 4 });
            _ = borders.AppendChild(new Ooxml.InsideVerticalBorder { Val = Ooxml.BorderValues.Single, Color = colorHex, Size = 4 });
            return borders;
        }

        private static Ooxml.TableCell BorderedCell(string text, string borderColorHex, string widthPct)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthPct, Type = Ooxml.TableWidthUnitValues.Pct });
            var cellBorders = new Ooxml.TableCellBorders();
            _ = cellBorders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = borderColorHex, Size = 4 });
            _ = cellBorders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = borderColorHex, Size = 4 });
            _ = cellBorders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = borderColorHex, Size = 4 });
            _ = cellBorders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = borderColorHex, Size = 4 });
            _ = cellProperties.AppendChild(cellBorders);
            _ = cell.AppendChild(cellProperties);

            var paragraph = new Ooxml.Paragraph();
            var boldPart = text.Contains(':', StringComparison.Ordinal) ? text[..(text.IndexOf(':', StringComparison.Ordinal) + 1)] : text;
            var rest = text[boldPart.Length..];
            _ = paragraph.AppendChild(CreateRun(boldPart, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            if (rest.Length > 0)
            {
                _ = paragraph.AppendChild(CreateRun(rest, bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            }

            _ = cell.AppendChild(paragraph);
            return cell;
        }

        // CT_RPr child order (ECMA-376 17.3.2): b, ..., color, spacing, w, kern, position, sz, szCs,
        // highlight, u, effect, bdr, shd, fitText, vertAlign, ... -- shd must come after color/sz, not
        // before, or Word's schema validator rejects the element and drops the shading on repair.
        private static Ooxml.RunProperties CreateRunProperties(bool bold, string colorHex, int fontSizeHalfPoints, string? shadeHex = null)
        {
            var runProperties = new Ooxml.RunProperties();
            if (bold)
            {
                _ = runProperties.AppendChild(new Ooxml.Bold());
            }

            _ = runProperties.AppendChild(new Ooxml.Color { Val = colorHex });
            _ = runProperties.AppendChild(new Ooxml.FontSize { Val = fontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });

            if (shadeHex is not null)
            {
                _ = runProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = shadeHex });
            }

            return runProperties;
        }

        private static Ooxml.Run CreateRun(string text, bool bold, string colorHex, int fontSizeHalfPoints, string? shadeHex = null)
        {
            var run = new Ooxml.Run();
            _ = run.AppendChild(CreateRunProperties(bold, colorHex, fontSizeHalfPoints, shadeHex));
            _ = run.AppendChild(new Ooxml.Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return run;
        }

        // ---- Images -----------------------------------------------------------------------------------

        internal static async Task<Ooxml.Drawing?> EmbedImageFromSourceAsync(
            MainDocumentPart mainPart, string src, Lazy<HttpClient>? httpClient, int? fixedWidthPx, int? fixedHeightPx)
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
            else if (httpClient is not null)
            {
                try
                {
                    bytes = await httpClient.Value.GetByteArrayAsync(src);
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

            return EmbedImageBytes(mainPart, bytes, fixedWidthPx, fixedHeightPx);
        }

        internal static Ooxml.Drawing? EmbedImageBytes(MainDocumentPart mainPart, byte[] bytes, int? fixedWidthPx, int? fixedHeightPx)
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                return null;
            }

            var widthPx = fixedWidthPx ?? bitmap.Width;
            var heightPx = fixedHeightPx ?? bitmap.Height;
            if (fixedWidthPx is null && widthPx > MaxImageWidthPx)
            {
                heightPx = (int)Math.Round(heightPx * (MaxImageWidthPx / (double)widthPx));
                widthPx = MaxImageWidthPx;
            }

            // Encode at the actual display resolution, not the source resolution -- otherwise a photo
            // uploaded at e.g. 640x640 but shown at 52x52 (a header logo) or capped to MaxImageWidthPx
            // still embeds its full original pixel data, needlessly bloating the .docx. Kept separate
            // from the outer `bitmap` (rather than aliasing it when no resize is needed) so the two
            // `using` locals never dispose the same instance twice.
            using var resizedBitmap = widthPx != bitmap.Width || heightPx != bitmap.Height
                ? bitmap.Resize(new SKImageInfo(widthPx, heightPx), SKSamplingOptions.Default)
                : null;
            var encodeSource = resizedBitmap ?? bitmap;

            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
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

            var relationshipId = mainPart.GetIdOfPart(imagePart);
            var widthEmu = widthPx * EmuPerPixel;
            var heightEmu = heightPx * EmuPerPixel;
            var drawingId = (uint)Interlocked.Increment(ref imageIdCounter);

            var blip = new A.Blip { Embed = relationshipId };
            var stretch = new A.Stretch();
            _ = stretch.AppendChild(new A.FillRectangle());
            var blipFill = new Pic.BlipFill();
            _ = blipFill.AppendChild(blip);
            _ = blipFill.AppendChild(stretch);

            var offset = new A.Offset { X = 0, Y = 0 };
            var extents = new A.Extents { Cx = widthEmu, Cy = heightEmu };
            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(offset);
            _ = transform2D.AppendChild(extents);
            var presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
            _ = presetGeometry.AppendChild(new A.AdjustValueList());
            var shapeProperties = new Pic.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(presetGeometry);

            var nonVisualDrawingProperties = new Pic.NonVisualDrawingProperties { Id = drawingId, Name = $"image{drawingId}.png" };
            var nonVisualPictureDrawingProperties = new Pic.NonVisualPictureDrawingProperties();
            var nonVisualPictureProperties = new Pic.NonVisualPictureProperties();
            _ = nonVisualPictureProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualPictureProperties.AppendChild(nonVisualPictureDrawingProperties);

            var picture = new Pic.Picture();
            _ = picture.AppendChild(nonVisualPictureProperties);
            _ = picture.AppendChild(blipFill);
            _ = picture.AppendChild(shapeProperties);

#pragma warning disable S1075 // spec-mandated OOXML namespace URI, not a configurable endpoint
            var graphicData = new A.GraphicData { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" };
#pragma warning restore S1075
            _ = graphicData.AppendChild(picture);
            var graphic = new A.Graphic();
            _ = graphic.AppendChild(graphicData);

            var extent = new Wp.Extent { Cx = widthEmu, Cy = heightEmu };
            var effectExtent = new Wp.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 };
            var docProperties = new Wp.DocProperties { Id = drawingId, Name = $"image{drawingId}.png" };
            var graphicFrameLocks = new Wp.NonVisualGraphicFrameDrawingProperties();

            var inline = new Wp.Inline { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 };
            _ = inline.AppendChild(extent);
            _ = inline.AppendChild(effectExtent);
            _ = inline.AppendChild(docProperties);
            _ = inline.AppendChild(graphicFrameLocks);
            _ = inline.AppendChild(graphic);

            var drawing = new Ooxml.Drawing();
            _ = drawing.AppendChild(inline);
            return drawing;
        }

        // ---- Page-level infrastructure (table style/page-break/section header-footer/watermark) -------

        /// <summary>
        /// Strips the "TableGrid" style HtmlToOpenXml used to assign every table by default -- kept here
        /// even though this builder never calls HtmlToOpenXml, as a defensive no-op: every table this
        /// builder creates already has no style reference, so there is nothing to strip; this exists in
        /// case any future table-building helper forgets to set one explicitly.
        /// </summary>
        private static void RemoveDefaultTableStyle(Ooxml.Body body)
        {
            foreach (var table in body.Descendants<Ooxml.Table>())
            {
                table.GetFirstChild<Ooxml.TableProperties>()?.GetFirstChild<Ooxml.TableStyle>()?.Remove();
            }
        }

        /// <summary>
        /// Marks every table row as non-splittable across a page break -- each question is one row (number
        /// + content), so without this Word can separate a question from its own answer choices when the
        /// page runs out of room mid-question.
        /// </summary>
        private static void PreventRowsSplittingAcrossPages(Ooxml.Body body)
        {
            foreach (var row in body.Descendants<Ooxml.TableRow>())
            {
                var properties = row.Elements<Ooxml.TableRowProperties>().FirstOrDefault();
                if (properties is null)
                {
                    properties = new Ooxml.TableRowProperties();
                    _ = row.PrependChild(properties);
                }

                if (!properties.Elements<Ooxml.CantSplit>().Any())
                {
                    _ = properties.AppendChild(new Ooxml.CantSplit());
                }
            }
        }

        /// <summary>
        /// Adds a slim branded running header (repeats on every page, natively -- that's what an OOXML
        /// header part is) and a footer with a real <c>PAGE</c>/<c>NUMPAGES</c> field (not hardcoded text --
        /// Word recalculates these fields itself as it paginates). If <paramref name="watermarkText"/> is
        /// set, its VML <c>v:textpath</c> shape (the same mechanism Word's own watermark feature uses,
        /// since OOXML has no simpler native watermark element) is folded into the same header part --
        /// a section can only have one default header, so this can't be a second, separate header.
        /// </summary>
        private static void AddPageHeaderAndFooter(MainDocumentPart mainPart, string? examTitle, string? watermarkText)
        {
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            var header = new Ooxml.Header();
            _ = header.AppendChild(BuildBrandTable(examTitle));
            if (!string.IsNullOrEmpty(watermarkText))
            {
                _ = header.AppendChild(BuildWatermarkParagraph(watermarkText));
            }

            headerPart.Header = header;
            headerPart.Header.Save();

            var footerPart = mainPart.AddNewPart<FooterPart>();
            var footer = new Ooxml.Footer();
            _ = footer.AppendChild(BuildFooterTable());
            footerPart.Footer = footer;
            footerPart.Footer.Save();

            var headerPartId = mainPart.GetIdOfPart(headerPart);
            var footerPartId = mainPart.GetIdOfPart(footerPart);
            var body = mainPart.Document!.Body!;
            var sectionProperties = body.Elements<Ooxml.SectionProperties>().FirstOrDefault();
            if (sectionProperties is null)
            {
                sectionProperties = new Ooxml.SectionProperties();
                _ = body.AppendChild(sectionProperties);
            }

            _ = sectionProperties.PrependChild(new Ooxml.FooterReference { Type = Ooxml.HeaderFooterValues.Default, Id = footerPartId });
            _ = sectionProperties.PrependChild(new Ooxml.HeaderReference { Type = Ooxml.HeaderFooterValues.Default, Id = headerPartId });
        }

        private static Ooxml.Table BuildBrandTable(string? examTitle)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            var tableBorders = new Ooxml.TableBorders();
            _ = tableBorders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = Yellow, Size = 12 });
            _ = tableProperties.AppendChild(tableBorders);
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 4513, 4513);

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildBorderlessCell(CreateRun("gamatrain", bold: true, colorHex: NavyDark, fontSizeHalfPoints: 18), Ooxml.JustificationValues.Left, "2500"));
            _ = row.AppendChild(BuildBorderlessCell(CreateRun(examTitle ?? string.Empty, bold: true, colorHex: NavyMid, fontSizeHalfPoints: 16), Ooxml.JustificationValues.Right, "2500"));
            _ = table.AppendChild(row);
            return table;
        }

        private static Ooxml.Table BuildFooterTable()
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            var tableBorders = new Ooxml.TableBorders();
            _ = tableBorders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = Yellow, Size = 12 });
            _ = tableProperties.AppendChild(tableBorders);
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 3009, 3009, 3008);

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildBorderlessCell(CreateRun("© gamatrain", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16), Ooxml.JustificationValues.Left, "1667"));
            _ = row.AppendChild(BuildBorderlessCell(CreateRun("gamatrain.com", bold: true, colorHex: TextDark, fontSizeHalfPoints: 16), Ooxml.JustificationValues.Center, "1667"));

            var pageParagraph = new Ooxml.Paragraph();
            var pageProperties = new Ooxml.ParagraphProperties();
            _ = pageProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Right });
            _ = pageParagraph.AppendChild(pageProperties);
            _ = pageParagraph.AppendChild(CreateRun("Page ", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16));
            _ = pageParagraph.AppendChild(BuildPageField("PAGE"));
            _ = pageParagraph.AppendChild(CreateRun(" of ", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16));
            _ = pageParagraph.AppendChild(BuildPageField("NUMPAGES"));

            var pageCell = new Ooxml.TableCell();
            var pageCellProperties = new Ooxml.TableCellProperties();
            _ = pageCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "1666", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = pageCellProperties.AppendChild(NoTableCellBorders());
            _ = pageCell.AppendChild(pageCellProperties);
            _ = pageCell.AppendChild(pageParagraph);
            _ = row.AppendChild(pageCell);

            _ = table.AppendChild(row);
            return table;
        }

        private static Ooxml.SimpleField BuildPageField(string instruction)
        {
            var field = new Ooxml.SimpleField { Instruction = instruction };
            _ = field.AppendChild(CreateRun("1", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16));
            return field;
        }

        private static Ooxml.TableCell BuildBorderlessCell(Ooxml.Run run, Ooxml.JustificationValues alignment, string widthPct)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthPct, Type = Ooxml.TableWidthUnitValues.Pct });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cell.AppendChild(cellProperties);

            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = alignment });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(run);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static Ooxml.Paragraph BuildWatermarkParagraph(string text)
        {
            var shape = new Vml.Shape(
                new Vml.Fill { Opacity = "0.5" },
                new Vml.TextPath
                {
                    Style = "font-family:'Calibri';font-size:1pt",
                    String = text,
                })
            {
                Style = "position:absolute;left:0;top:0;width:415pt;height:207.5pt;rotation:315;z-index:-251658752;" +
                    "mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin",
                FillColor = "#4472c4",
                Stroked = false,
            };

            var picture = new Ooxml.Picture();
            _ = picture.AppendChild(shape);
            var run = new Ooxml.Run();
            _ = run.AppendChild(picture);
            var paragraph = new Ooxml.Paragraph();
            _ = paragraph.AppendChild(run);
            return paragraph;
        }
    }
}
