namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
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
    using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

    /// <summary>
    /// Builds an exam Word document entirely via native OOXML -- no HtmlToOpenXml conversion layer.
    /// Exists because that layer couldn't produce genuinely native-quality Word tables (silently applied
    /// its own default table style, mishandled bare-pixel widths, etc. -- see
    /// docs/business/exams-and-content.md); every visual property here is set directly, matching
    /// exam.word.html's design (same colors/layout) without going through HTML/CSS at all.
    /// </summary>
    /// <param name="GamaWordmark">
    /// The header's brand panel (exam-gama-wordmark.png): the dark diagonal-cut panel with the white "Gama" logo
    /// already drawn on it, as one picture. The panel is also drawn by the header background shapes for viewers
    /// that support them (Word, LibreOffice), so there it just sits exactly on top of the same shape; in Google
    /// Docs, which skips those shapes entirely, this picture is what still shows the dark panel behind the white
    /// logo - a separate dark-logo fallback via mc:AlternateContent didn't work, Google ignores the Fallback.
    /// </param>
    /// <param name="ProfilePlaceholder">
    /// Generic silhouette icon (exam-profile-placeholder.png) shown where the reference template has a real
    /// author's photo -- ExamDto has no per-exam author-photo field, and this codebase never bakes a real
    /// person's photo into a public repo, so a neutral placeholder fills that visual slot instead of leaving
    /// it blank.
    /// </param>
    /// <param name="FooterWave">The decorative wave shape at the bottom of every page (exam-footer-wave.png).</param>
    /// <param name="FooterGlobe">The globe icon next to the footer's website link (exam-footer-globe.png).</param>
    internal sealed record HeaderBrandAssets(byte[] GamaWordmark, byte[] ProfilePlaceholder, byte[] FooterWave, byte[] FooterGlobe);

    internal static partial class ExamWordDocumentBuilder
    {
        private const long EmuPerPixel = 9525; // 96dpi CSS px -> EMU
        private const int MaxImageWidthPx = 500;

        /// <summary>Cap for a question's own image when it sits beside the text (see BuildQuestionTableAsync)
        /// instead of below it -- that column is ~2300dxa (~1.6in) wide, well under MaxImageWidthPx.</summary>
        private const int MaxQuestionSideImageWidthPx = 150;

        /// <summary>Cap for one thumbnail in an all-image options row (see BuildImageOptionCellAsync) --
        /// each cell is ~1950dxa (~1.35in) wide.</summary>
        private const int MaxImageOptionWidthPx = 120;
        private const string Yellow = "F6B500";
        private const string BorderGray = "9AABBA";
        private const string BorderLightGray = "C5D0DB";
        private const string RowGrayBg = "F4F7FB";
        private const string TextDark = "172033";
        private const string TextMuted = "5B6777";

        // Light-grey badge style (question number / option number) -- replaces the earlier dark-navy-filled
        // badge to match the current brand template (badges read as a neutral chip, not an accent color).
        private const string BadgeGray = "E7ECF2";

        /// <summary>
        /// An option's plain-text length (HTML stripped) above which the 2x2 options grid switches to a
        /// single stacked column -- matches the reference template, where short options ("ampere") sit
        /// two-per-row but longer ones ("concerned about the level...") each get a full-width row instead
        /// of being squeezed into a half-width cell.
        /// </summary>
        private const int LongOptionTextThreshold = 28;

        // Only needs document-local uniqueness (OOXML drawing IDs aren't referenced across files), so a
        // simple incrementing counter is enough -- Random would be gratuitous and trips CA5394.
        private static long imageIdCounter;

        public static async Task<byte[]> BuildAsync(
            [NotNull] ExamInformationResponseDto data, HeaderBrandAssets brandAssets, string? watermarkText, Lazy<HttpClient> httpClient)
        {
            using MemoryStream stream = new();
            using (var wordDocument = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = wordDocument.AddMainDocumentPart();
                var body = new Ooxml.Body();
                var document = new Ooxml.Document();
                _ = document.AppendChild(body);
                mainPart.Document = document;

                if (data.Tests is not null)
                {
                    for (var i = 0; i < data.Tests.Count; i++)
                    {
                        _ = body.AppendChild(new Ooxml.Paragraph());
                        _ = body.AppendChild(await BuildQuestionTableAsync(data.Tests[i], i, mainPart, httpClient));
                    }

                    AppendAnswerKeySection(body, data.Tests);
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

                await AddPageHeaderAndFooterAsync(mainPart, data.Exam, watermarkText, brandAssets);

                mainPart.Document.Save();
            }

            return stream.ToArray();
        }

        // ---- Header / title / metadata --------------------------------------------------------------

        /// <summary>
        /// Header as a plain bordered table (thin light-gray grid lines, white background) -- matches the
        /// reference template's own actual appearance (no color fills at all; an earlier revision assumed
        /// dark/light shaded cells from exam.word.html's CSS design, but the reference .docx itself uses a
        /// traditional bordered-table look). Row/cell grouping matches the reference's real field layout,
        /// confirmed by reading its raw header1.xml cell-by-cell: logo | portrait+author-info | QR, then
        /// title+Date, then Name/School/Questions/Time/Level. The portrait+author-info cell intentionally
        /// stays empty here -- ExamDto has no author-name or photo field, and the reference's own values
        /// there ("By: zivar Sushu", a real person's photo) are that one sample document's actual content,
        /// not generic template data; hardcoding a real person's name/photo into this shipped codebase would
        /// bake their PII into a public repo. The cell/column is kept (not removed) so the layout still
        /// matches once such a field exists.
        /// </summary>
        private static async Task<Ooxml.Table> BuildHeaderRowAsync<TPart>(
            ExamInformationResponseDto.ExamDto? exam, TPart headerPart, HeaderBrandAssets brandAssets)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            const int contentWidthDxa = 9026;

            // A shared fine-grained 20-column grid lets each row group columns differently via gridSpan
            // (logo/portrait/text/QR in row 1; title/Date in row 2; Name/School/.../Level in row 3) while
            // all rows still reference the same underlying tblGrid, same technique the reference's own
            // header1.xml uses (its real grid has even more columns, pct-based rather than dxa-based).
            const int columnCount = 20;
            const int columnUnitDxa = contentWidthDxa / columnCount; // 451
            const int lastColumnDxa = contentWidthDxa - (columnUnitDxa * (columnCount - 1)); // absorbs the rounding remainder

            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = contentWidthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = tableProperties.AppendChild(LightGrayTableBorders());
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            var columnWidths = Enumerable.Repeat(columnUnitDxa, columnCount - 1).Append(lastColumnDxa).ToArray();
            AppendTableGrid(table, columnWidths);

            static int SpanWidth(int[] widths, int startColumn, int columnSpan) => widths.Skip(startColumn).Take(columnSpan).Sum();

            // Row 1: logo (9 cols, ~45%) | portrait (2 cols) | author info (5 cols) | QR (4 cols, ~20%).
            var brandRow = new Ooxml.TableRow();

            // 270x52 px = the 540x104 asset at 96dpi/2, matching the dark-panel shape's own 249x48 reference
            // units at the header table's scale so the picture lines up with the shape behind it.
            var wordmarkDrawing = EmbedImageBytes(headerPart, brandAssets.GamaWordmark, 270, 52);
            var wordmarkParagraph = new Ooxml.Paragraph();
            if (wordmarkDrawing is not null)
            {
                var wordmarkRun = new Ooxml.Run();
                _ = wordmarkRun.AppendChild(wordmarkDrawing);
                _ = wordmarkParagraph.AppendChild(wordmarkRun);
            }

            var wordmarkCell = BorderedGridSpanCell(wordmarkParagraph, Ooxml.JustificationValues.Left, 9, SpanWidth(columnWidths, 0, 9).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Top);
            // No left padding, so the picture's own dark panel starts flush with the shape behind it (Word).
            var wordmarkCellProperties = wordmarkCell.GetFirstChild<Ooxml.TableCellProperties>()!;
            var wordmarkMargin = new Ooxml.TableCellMargin();
            _ = wordmarkMargin.AppendChild(new Ooxml.LeftMargin { Width = "0", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = wordmarkCellProperties.GetFirstChild<Ooxml.TableCellVerticalAlignment>()!.InsertBeforeSelf(wordmarkMargin);
            _ = brandRow.AppendChild(wordmarkCell);

            // No per-exam author photo (see this method's doc comment) -- a neutral silhouette fills the
            // slot instead of leaving it blank, same idea as any app's default avatar.
            const int profileImagePx = 45; // square, matches the placeholder asset's own 1:1 aspect ratio
            var profileDrawing = EmbedImageBytes(headerPart, brandAssets.ProfilePlaceholder, profileImagePx, profileImagePx);
            var profileParagraph = new Ooxml.Paragraph();
            if (profileDrawing is not null)
            {
                var profileRun = new Ooxml.Run();
                _ = profileRun.AppendChild(profileDrawing);
                _ = profileParagraph.AppendChild(profileRun);
            }

            _ = brandRow.AppendChild(BorderedGridSpanCell(profileParagraph, Ooxml.JustificationValues.Center, 2, SpanWidth(columnWidths, 9, 2).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            // "By:" label ready to show a real author name once ExamDto has that field -- no such field
            // exists yet, so the value stays blank rather than fabricated (see this method's doc comment).
            var authorParagraph = new Ooxml.Paragraph();
            _ = authorParagraph.AppendChild(CreateRun("By: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = brandRow.AppendChild(BorderedGridSpanCell(authorParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 11, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var qrRun = new Ooxml.Run();
            if (!string.IsNullOrEmpty(exam?.QrCode))
            {
                var qrDrawing = await EmbedImageFromSourceAsync(headerPart, exam.QrCode, null, 50, 50);
                if (qrDrawing is not null)
                {
                    _ = qrRun.AppendChild(qrDrawing);
                }
            }

            _ = brandRow.AppendChild(BorderedGridSpanCell(WrapInParagraph(qrRun, Ooxml.JustificationValues.Center), Ooxml.JustificationValues.Center, 4, SpanWidth(columnWidths, 16, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));
            _ = table.AppendChild(brandRow);

            // Row 2: title (15 cols, ~75%) | "Date:" label (2 cols) | date value (3 cols).
            var titleDateRow = new Ooxml.TableRow();
            var titleParagraph = new Ooxml.Paragraph();
            _ = titleParagraph.AppendChild(CreateRun(exam?.Title ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 24));
            _ = titleDateRow.AppendChild(BorderedGridSpanCell(titleParagraph, Ooxml.JustificationValues.Left, 15, SpanWidth(columnWidths, 0, 15).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var dateLabelParagraph = new Ooxml.Paragraph();
            _ = dateLabelParagraph.AppendChild(CreateRun("Date:", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = titleDateRow.AppendChild(BorderedGridSpanCell(dateLabelParagraph, Ooxml.JustificationValues.Left, 2, SpanWidth(columnWidths, 15, 2).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var dateValueParagraph = new Ooxml.Paragraph();
            _ = dateValueParagraph.AppendChild(CreateRun(exam?.StartDate ?? string.Empty, bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = titleDateRow.AppendChild(BorderedGridSpanCell(dateValueParagraph, Ooxml.JustificationValues.Left, 3, SpanWidth(columnWidths, 17, 3).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));
            _ = table.AppendChild(titleDateRow);

            // Row 3: Name (5) | School (5) | empty spacer (1, matching the reference's own gap column) |
            // Questions (3) | Time (3) | Level (3).
            var metadataRow = new Ooxml.TableRow();

            var nameParagraph = new Ooxml.Paragraph();
            _ = nameParagraph.AppendChild(CreateRun("Name:", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(nameParagraph, Ooxml.JustificationValues.Left, 4, SpanWidth(columnWidths, 0, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var schoolParagraph = new Ooxml.Paragraph();
            _ = schoolParagraph.AppendChild(CreateRun("School:", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(schoolParagraph, Ooxml.JustificationValues.Left, 4, SpanWidth(columnWidths, 4, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            _ = metadataRow.AppendChild(BorderedGridSpanCell(new Ooxml.Paragraph(), Ooxml.JustificationValues.Left, 1, SpanWidth(columnWidths, 8, 1).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var questionsParagraph = new Ooxml.Paragraph();
            _ = questionsParagraph.AppendChild(CreateRun("Questions: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = questionsParagraph.AppendChild(CreateRun(exam?.TestsCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(questionsParagraph, Ooxml.JustificationValues.Left, 4, SpanWidth(columnWidths, 9, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var timeParagraph = new Ooxml.Paragraph();
            _ = timeParagraph.AppendChild(CreateRun("Time: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = timeParagraph.AppendChild(CreateRun($"{exam?.ExamTime} min", bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(timeParagraph, Ooxml.JustificationValues.Left, 4, SpanWidth(columnWidths, 13, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));

            var levelParagraph = new Ooxml.Paragraph();
            _ = levelParagraph.AppendChild(CreateRun("Level: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = levelParagraph.AppendChild(CreateRun(exam?.ScoreType ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(levelParagraph, Ooxml.JustificationValues.Left, 3, SpanWidth(columnWidths, 17, 3).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center));
            _ = table.AppendChild(metadataRow);

            return table;
        }

        /// <summary>Thin light-gray grid lines on every side, matching the reference's traditional bordered-table look (no shading/fill).</summary>
        private static Ooxml.TableCell BorderedGridSpanCell(Ooxml.Paragraph paragraph, Ooxml.JustificationValues alignment, int gridSpan, string widthDxa, Ooxml.TableVerticalAlignmentValues verticalAlignment)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(new Ooxml.GridSpan { Val = gridSpan });
            _ = cellProperties.AppendChild(LightGrayCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = verticalAlignment });
            _ = cell.AppendChild(cellProperties);
            var properties = ZeroSpacingParagraphProperties(paragraph);
            if (properties.Elements<Ooxml.Justification>().FirstOrDefault() is null)
            {
                var justification = new Ooxml.Justification { Val = alignment };
                var existingMarkRunProperties = properties.Elements<Ooxml.ParagraphMarkRunProperties>().FirstOrDefault();
                _ = existingMarkRunProperties is not null
                    ? existingMarkRunProperties.InsertBeforeSelf(justification)
                    : properties.AppendChild(justification);
            }

            _ = cell.AppendChild(paragraph);
            return cell;
        }

        /// <summary>Cell-level equivalent of <see cref="LightGrayTableBorders"/> -- explicit thin light-gray on all four sides.</summary>
        private static Ooxml.TableCellBorders LightGrayCellBorders()
        {
            var borders = new Ooxml.TableCellBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            return borders;
        }

        /// <summary>Table-level equivalent of <see cref="LightGrayCellBorders"/> -- thin light-gray on all outer/inside sides.</summary>
        private static Ooxml.TableBorders LightGrayTableBorders()
        {
            var borders = new Ooxml.TableBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.InsideHorizontalBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.InsideVerticalBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            return borders;
        }

        /// <summary>
        /// Decorative header background as native DrawingML vector shapes (<c>wps:wsp</c>/<c>a:custGeom</c>),
        /// floated <c>behindDoc="1"</c> behind the header table -- not a raster image, so this is a genuinely
        /// separate attempt from the earlier floating-PNG-banner approach that was dropped for hiding table
        /// text unreliably across renderers. Path geometry is the reference template's own real SVG source
        /// (image4.svg, extracted from its .docx -- see docs/business/exams-and-content.md), not a redrawn
        /// approximation: 4 flat-fill shapes in a shared 547x104 coordinate space (dark panel, light panel,
        /// and two stacked light bars beneath them), each independently scaled to the actual header table's
        /// width via its own <c>a:path</c> w/h, all anchored at the same page-relative position so they line
        /// up. This paragraph is inserted before the table (not modifying the table itself, which already
        /// has no cell shading and needs no change for the background to show through).
        /// </summary>
        private static Ooxml.Paragraph BuildHeaderBackgroundParagraph()
        {
            const int contentWidthDxa = 9026; // matches BuildHeaderRowAsync's own table width
            const int referenceWidth = 547;
            const int referenceHeight = 104;
            const long dxaToEmu = 635;
            const int pageLeftMarginDxa = 1440;
            const int headerDistanceDxa = 720;

            var widthEmu = contentWidthDxa * dxaToEmu;
            var heightEmu = (long)Math.Round(widthEmu * ((double)referenceHeight / referenceWidth));
            var leftOffsetEmu = pageLeftMarginDxa * dxaToEmu;
            var topOffsetEmu = headerDistanceDxa * dxaToEmu;

            // Floating (behindDoc) drawings don't contribute to the paragraph's own line-height metrics --
            // this paragraph is otherwise empty, so without shrinking its mark run's own font size it still
            // takes up a default (~11pt) line, pushing the table below it down and away from where the
            // shapes are actually anchored (page-relative, matching the table's own true top). A near-zero
            // mark font size collapses that gap.
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = Ooxml.LineSpacingRuleValues.Auto });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);

            var run = new Ooxml.Run();
            var runProperties = new Ooxml.RunProperties();
            _ = runProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = run.AppendChild(runProperties);

            // Path data below is the reference's own image4.svg, transcribed path-for-path (SVG's decimal
            // coordinates rounded to the nearest integer -- DrawingML path coordinates are plain integers).

            // Shape 1: subtle off-white band (#F9FAFB), y:56-104, all 4 corners rounded.
            _ = run.AppendChild(BuildBackgroundShapeDrawing(
                "F9FAFB", widthEmu, heightEmu, leftOffsetEmu, topOffsetEmu, referenceWidth, referenceHeight,
                [
                    PathCommand.Move(0, 64),
                    PathCommand.Cubic(0, 60, 4, 56, 8, 56),
                    PathCommand.Line(539, 56),
                    PathCommand.Cubic(543, 56, 547, 60, 547, 64),
                    PathCommand.Line(547, 96),
                    PathCommand.Cubic(547, 100, 543, 104, 539, 104),
                    PathCommand.Line(8, 104),
                    PathCommand.Cubic(4, 104, 0, 100, 0, 96),
                    PathCommand.Close(),
                ]));

            // Shape 2: dark charcoal panel (#24292F), rounded left corners, diagonal-cut right edge.
            _ = run.AppendChild(BuildBackgroundShapeDrawing(
                "24292F", widthEmu, heightEmu, leftOffsetEmu, topOffsetEmu, referenceWidth, referenceHeight,
                [
                    PathCommand.Move(0, 8),
                    PathCommand.Cubic(0, 4, 4, 0, 8, 0),
                    PathCommand.Line(219, 0),
                    PathCommand.Cubic(225, 0, 230, 3, 233, 8),
                    PathCommand.Line(249, 36),
                    PathCommand.Cubic(252, 41, 248, 48, 242, 48),
                    PathCommand.Line(8, 48),
                    PathCommand.Cubic(4, 48, 0, 44, 0, 40),
                    PathCommand.Close(),
                ]));

            // Shape 3: light gray panel (#F2F4F7), rounded right corners, complementary diagonal-cut left edge.
            _ = run.AppendChild(BuildBackgroundShapeDrawing(
                "F2F4F7", widthEmu, heightEmu, leftOffsetEmu, topOffsetEmu, referenceWidth, referenceHeight,
                [
                    PathCommand.Move(491, 40),
                    PathCommand.Cubic(491, 44, 487, 48, 483, 48),
                    PathCommand.Line(272, 48),
                    PathCommand.Cubic(266, 48, 261, 45, 258, 40),
                    PathCommand.Line(242, 12),
                    PathCommand.Cubic(239, 7, 243, 0, 249, 0),
                    PathCommand.Line(483, 0),
                    PathCommand.Cubic(487, 0, 491, 4, 491, 8),
                    PathCommand.Close(),
                ]));

            // Shape 4: light gray lower bar (#F2F4F7), y:80-104, flat top / rounded bottom corners.
            _ = run.AppendChild(BuildBackgroundShapeDrawing(
                "F2F4F7", widthEmu, heightEmu, leftOffsetEmu, topOffsetEmu, referenceWidth, referenceHeight,
                [
                    PathCommand.Move(0, 96),
                    PathCommand.Cubic(0, 100, 4, 104, 8, 104),
                    PathCommand.Line(539, 104),
                    PathCommand.Cubic(543, 104, 547, 100, 547, 96),
                    PathCommand.Line(547, 80),
                    PathCommand.Line(0, 80),
                    PathCommand.Close(),
                ]));

            _ = paragraph.AppendChild(run);
            return paragraph;
        }

        /// <summary>One segment of an <c>a:custGeom</c> path -- a tiny DSL so each shape's real SVG path data (see BuildHeaderBackgroundParagraph) reads the same shape as the source it was transcribed from.</summary>
        private readonly record struct PathCommand(char Kind, int[] Coordinates)
        {
            public static PathCommand Move(int x, int y) => new('M', [x, y]);
            public static PathCommand Line(int x, int y) => new('L', [x, y]);
            public static PathCommand Cubic(int x1, int y1, int x2, int y2, int x3, int y3) => new('C', [x1, y1, x2, y2, x3, y3]);
            public static PathCommand Close() => new('Z', []);
        }

        private static AlternateContent BuildBackgroundShapeDrawing(
            string fillHex, long widthEmu, long heightEmu, long leftOffsetEmu, long topOffsetEmu,
            int pathWidth, int pathHeight, PathCommand[] commands)
        {
            // Shares imageIdCounter with the picture-embedding helpers (BuildImageGraphic) -- docPr/wp:anchor
            // ids must be unique across the whole document, not just among these 4 shapes, or Word's
            // validator (unlike LibreOffice) flags duplicate ids.
            var drawingId = (uint)Interlocked.Increment(ref imageIdCounter);
            var path = new A.Path { Width = pathWidth, Height = pathHeight, Fill = A.PathFillModeValues.Norm, Stroke = false };
            foreach (var command in commands)
            {
                switch (command.Kind)
                {
                    case 'M':
                        var moveTo = new A.MoveTo();
                        _ = moveTo.AppendChild(new A.Point { X = command.Coordinates[0].ToString(CultureInfo.InvariantCulture), Y = command.Coordinates[1].ToString(CultureInfo.InvariantCulture) });
                        _ = path.AppendChild(moveTo);
                        break;
                    case 'L':
                        var lineTo = new A.LineTo();
                        _ = lineTo.AppendChild(new A.Point { X = command.Coordinates[0].ToString(CultureInfo.InvariantCulture), Y = command.Coordinates[1].ToString(CultureInfo.InvariantCulture) });
                        _ = path.AppendChild(lineTo);
                        break;
                    case 'C':
                        var cubicBezier = new A.CubicBezierCurveTo();
                        _ = cubicBezier.AppendChild(new A.Point { X = command.Coordinates[0].ToString(CultureInfo.InvariantCulture), Y = command.Coordinates[1].ToString(CultureInfo.InvariantCulture) });
                        _ = cubicBezier.AppendChild(new A.Point { X = command.Coordinates[2].ToString(CultureInfo.InvariantCulture), Y = command.Coordinates[3].ToString(CultureInfo.InvariantCulture) });
                        _ = cubicBezier.AppendChild(new A.Point { X = command.Coordinates[4].ToString(CultureInfo.InvariantCulture), Y = command.Coordinates[5].ToString(CultureInfo.InvariantCulture) });
                        _ = path.AppendChild(cubicBezier);
                        break;
                    case 'Z':
                        _ = path.AppendChild(new A.CloseShapePath());
                        break;
                }
            }

            var pathList = new A.PathList();
            _ = pathList.AppendChild(path);
            var customGeometry = new A.CustomGeometry();
            _ = customGeometry.AppendChild(new A.AdjustValueList());
            _ = customGeometry.AppendChild(pathList);

            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(new A.Offset { X = 0, Y = 0 });
            _ = transform2D.AppendChild(new A.Extents { Cx = widthEmu, Cy = heightEmu });

            var shapeProperties = new Wps.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(customGeometry);
            var solidFill = new A.SolidFill();
            _ = solidFill.AppendChild(new A.RgbColorModelHex { Val = fillHex });
            _ = shapeProperties.AppendChild(solidFill);
            var outline = new A.Outline();
            _ = outline.AppendChild(new A.NoFill());
            _ = shapeProperties.AppendChild(outline);

            var shape = new Wps.WordprocessingShape();
            _ = shape.AppendChild(new Wps.NonVisualDrawingProperties { Id = drawingId, Name = $"Header background {drawingId}" });
            _ = shape.AppendChild(new Wps.NonVisualDrawingShapeProperties());
            _ = shape.AppendChild(shapeProperties);
            _ = shape.AppendChild(new Wps.TextBodyProperties());

#pragma warning disable S1075 // spec-mandated OOXML namespace URI, not a configurable endpoint
            var graphicData = new A.GraphicData { Uri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape" };
#pragma warning restore S1075
            _ = graphicData.AppendChild(shape);
            var graphic = new A.Graphic();
            _ = graphic.AppendChild(graphicData);

            var anchor = new Wp.Anchor
            {
                SimplePos = false,
                RelativeHeight = drawingId,
                BehindDoc = true,
                Locked = false,
                LayoutInCell = true,
                AllowOverlap = true,
            };
            _ = anchor.AppendChild(new Wp.SimplePosition { X = 0, Y = 0 });
            var horizontalPosition = new Wp.HorizontalPosition { RelativeFrom = Wp.HorizontalRelativePositionValues.Page };
            _ = horizontalPosition.AppendChild(new Wp.PositionOffset(leftOffsetEmu.ToString(CultureInfo.InvariantCulture)));
            _ = anchor.AppendChild(horizontalPosition);
            var verticalPosition = new Wp.VerticalPosition { RelativeFrom = Wp.VerticalRelativePositionValues.Page };
            _ = verticalPosition.AppendChild(new Wp.PositionOffset(topOffsetEmu.ToString(CultureInfo.InvariantCulture)));
            _ = anchor.AppendChild(verticalPosition);
            _ = anchor.AppendChild(new Wp.Extent { Cx = widthEmu, Cy = heightEmu });
            _ = anchor.AppendChild(new Wp.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 });
            _ = anchor.AppendChild(new Wp.WrapNone());
            _ = anchor.AppendChild(new Wp.DocProperties { Id = drawingId, Name = $"Header background {drawingId}" });
            _ = anchor.AppendChild(new Wp.NonVisualGraphicFrameDrawingProperties());
            _ = anchor.AppendChild(graphic);

            var drawing = new Ooxml.Drawing();
            _ = drawing.AppendChild(anchor);

            // Word itself always writes wps shapes inside mc:AlternateContent; Google Docs' importer rejects
            // the whole file ("File could not open") when a bare wps shape sits directly in a run. An empty
            // Fallback lets importers that don't understand wps simply skip the decoration.
            var choice = new AlternateContentChoice { Requires = "wps" };
            _ = choice.AppendChild(drawing);
            var alternateContent = new AlternateContent();
            _ = alternateContent.AppendChild(choice);
            _ = alternateContent.AppendChild(new AlternateContentFallback());
            return alternateContent;
        }

        /// <summary>
        /// Returns the paragraph's existing <c>w:pPr</c> (never adding a second one - a paragraph with two
        /// is invalid OOXML) with spacing-before/after zeroed and single line spacing forced, creating one
        /// if none exists yet. Needed because the header's brand/title/QR rows use "exact" heights tight
        /// enough that Word's Normal-style default paragraph spacing (commonly ~8-10pt after) alone pushes
        /// content past the declared row height - and unlike a genuine overflow, LibreOffice (used to
        /// verify these renders) doesn't grow the row to compensate, it paints the next row at its declared
        /// position regardless, which reads as the two rows' content overlapping.
        /// </summary>
        private static Ooxml.ParagraphProperties ZeroSpacingParagraphProperties(Ooxml.Paragraph paragraph)
        {
            var properties = paragraph.Elements<Ooxml.ParagraphProperties>().FirstOrDefault();
            if (properties is null)
            {
                properties = new Ooxml.ParagraphProperties();
                _ = paragraph.InsertAt(properties, 0);
            }

            // CT_PPrBase allows at most one w:spacing -- some callers (e.g. the header banner's anchor
            // paragraph) already set one explicitly before calling this, so adding a second unconditionally
            // produced a duplicate that Word (unlike LibreOffice) flagged as document corruption. Leave the
            // caller's own value alone rather than overwrite it.
            if (properties.Elements<Ooxml.SpacingBetweenLines>().FirstOrDefault() is null)
            {
                var spacing = new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = Ooxml.LineSpacingRuleValues.Auto };

                // CT_PPrBase (ECMA-376 17.3.1.26) requires spacing before jc - appending blindly would put it
                // after an already-set Justification (e.g. from WrapInParagraph), which Word's schema
                // validator rejects and silently drops on repair rather than just reordering it.
                var existingJustification = properties.Elements<Ooxml.Justification>().FirstOrDefault();
                _ = existingJustification is not null ? existingJustification.InsertBeforeSelf(spacing) : properties.AppendChild(spacing);
            }

            return properties;
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
            var hasQuestionImage = !string.IsNullOrEmpty(test.QuestionFile);

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

            // Matches the reference template: a question with its own image puts that image in a dedicated
            // right-hand column beside the text/options, rather than stacking it below the question text --
            // a 3rd sibling cell in this same row, not a nested table (a nested table inside a question's
            // content cell was already tried elsewhere in this file and dropped for unreliable width/
            // page-break behavior - see BuildQuestionTableAsync's sibling-table note above BuildOptionsGridAsync).
            if (hasQuestionImage)
            {
                AppendTableGrid(table, 722, 6004, 2300);
            }
            else
            {
                AppendTableGrid(table, 722, 8304);
            }

            var rowBg = index % 2 == 0 ? "FFFFFF" : RowGrayBg;

            var row = new Ooxml.TableRow();

            var numberParagraph = new Ooxml.Paragraph();
            _ = numberParagraph.AppendChild(CreateRun($" {index + 1} ", bold: true, colorHex: TextDark, fontSizeHalfPoints: 22, shadeHex: BadgeGray));
            var numberCell = new Ooxml.TableCell();
            _ = numberCell.AppendChild(new Ooxml.TableCellProperties(
                new Ooxml.TableCellWidth { Width = "400", Type = Ooxml.TableWidthUnitValues.Pct },
                new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = RowGrayBg },
                new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Top }));
            _ = numberCell.AppendChild(numberParagraph);
            _ = row.AppendChild(numberCell);

            var contentCell = new Ooxml.TableCell();
            var contentCellProperties = new Ooxml.TableCellProperties();
            _ = contentCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = hasQuestionImage ? "3325" : "4600", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = contentCellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = rowBg });
            _ = contentCell.AppendChild(contentCellProperties);

            var questionFormat = new ExamWordRichText.RunFormat(true, false, false, false, false, TextDark, 22);
            var questionParagraphs = await ExamWordRichText.ParseToParagraphsAsync(test.Question, mainPart, httpClient, questionFormat);
            foreach (var paragraph in questionParagraphs)
            {
                _ = contentCell.AppendChild(paragraph);
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

            if (hasQuestionImage)
            {
                var imageCell = new Ooxml.TableCell();
                var imageCellProperties = new Ooxml.TableCellProperties();
                _ = imageCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "1275", Type = Ooxml.TableWidthUnitValues.Pct });
                _ = imageCellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = rowBg });
                _ = imageCellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
                _ = imageCell.AppendChild(imageCellProperties);

                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, test.QuestionFile!, httpClient, MaxQuestionSideImageWidthPx, null);
                var imageParagraph = new Ooxml.Paragraph();
                var imageParagraphProperties = new Ooxml.ParagraphProperties();
                _ = imageParagraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
                _ = imageParagraph.AppendChild(imageParagraphProperties);
                if (imageDrawing is not null)
                {
                    var imageRun = new Ooxml.Run();
                    _ = imageRun.AppendChild(imageDrawing);
                    _ = imageParagraph.AppendChild(imageRun);
                }

                _ = imageCell.AppendChild(imageParagraph);
                _ = row.AppendChild(imageCell);
            }

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

            // Matches the reference template: short options (e.g. "ampere") sit two-per-row, but once any
            // option's text runs long, every option instead gets its own full-width row -- a long option
            // squeezed into a half-width cell wraps awkwardly and no longer lines up with its neighbor.
            var useSingleColumn = PlainTextLength(test.OptionA) > LongOptionTextThreshold
                || PlainTextLength(test.OptionB) > LongOptionTextThreshold
                || PlainTextLength(test.OptionC) > LongOptionTextThreshold
                || PlainTextLength(test.OptionD) > LongOptionTextThreshold;

            var options = new[]
            {
                (Number: "1", Html: test.OptionA, File: test.OptionAFile),
                (Number: "2", Html: test.OptionB, File: test.OptionBFile),
                (Number: "3", Html: test.OptionC, File: test.OptionCFile),
                (Number: "4", Html: test.OptionD, File: test.OptionDFile),
            };

            // Matches the reference template: when every option is an image (no meaningful text), all four
            // sit side by side in one row (badge above its own thumbnail) instead of the text grid/stack --
            // a diagram-sized image doesn't wrap the way text does, so there's no reason to give it a whole
            // row to itself the way a long text option needs.
            var isImageOptions = options.All(o => !string.IsNullOrEmpty(o.File) && PlainTextLength(o.Html) == 0);
            if (isImageOptions)
            {
                AppendTableGrid(table, 1950, 1950, 1950, 1950);
                var imageRow = new Ooxml.TableRow();
                foreach (var option in options)
                {
                    _ = imageRow.AppendChild(await BuildImageOptionCellAsync(option.Number, option.File, mainPart, httpClient, "1950"));
                }

                _ = table.AppendChild(imageRow);
                return table;
            }

            if (useSingleColumn)
            {
                AppendTableGrid(table, 450, 7350);
                foreach (var option in options)
                {
                    var row = new Ooxml.TableRow();
                    _ = row.AppendChild(BuildOptionBadgeCell(option.Number));
                    _ = row.AppendChild(await BuildOptionContentCellAsync(option.Html, option.File, mainPart, httpClient, "7350"));
                    _ = table.AppendChild(row);
                }
            }
            else
            {
                AppendTableGrid(table, 450, 3450, 450, 3450);
                for (var i = 0; i < options.Length; i += 2)
                {
                    var row = new Ooxml.TableRow();
                    _ = row.AppendChild(BuildOptionBadgeCell(options[i].Number));
                    _ = row.AppendChild(await BuildOptionContentCellAsync(options[i].Html, options[i].File, mainPart, httpClient, "3450"));
                    _ = row.AppendChild(BuildOptionBadgeCell(options[i + 1].Number));
                    _ = row.AppendChild(await BuildOptionContentCellAsync(options[i + 1].Html, options[i + 1].File, mainPart, httpClient, "3450"));
                    _ = table.AppendChild(row);
                }
            }

            return table;
        }

        /// <summary>Rough HTML-stripped length used only to pick the options layout -- not real sanitization.</summary>
        private static int PlainTextLength(string? html) =>
            string.IsNullOrWhiteSpace(html) ? 0 : HtmlTagRegex().Replace(html, string.Empty).Trim().Length;

        [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
        private static partial System.Text.RegularExpressions.Regex HtmlTagRegex();

        private static Ooxml.TableCell BuildOptionBadgeCell(string number)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(CreateRun(number, bold: true, colorHex: TextDark, fontSizeHalfPoints: 18));

            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "450", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = BadgeGray });
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static async Task<Ooxml.TableCell> BuildOptionContentCellAsync(
            string? optionHtml, string? optionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, string widthDxa)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
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

        /// <summary>One cell of the image-options row: badge number centered above its own thumbnail,
        /// not the badge+content two-cell pair the text layouts use -- a diagram doesn't need a separate
        /// badge column since there's no wrapping text to align it against.</summary>
        private static async Task<Ooxml.TableCell> BuildImageOptionCellAsync(
            string number, string? optionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, string widthDxa)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cell.AppendChild(cellProperties);

            var numberParagraph = new Ooxml.Paragraph();
            var numberParagraphProperties = new Ooxml.ParagraphProperties();
            _ = numberParagraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = numberParagraph.AppendChild(numberParagraphProperties);
            _ = numberParagraph.AppendChild(CreateRun(number, bold: true, colorHex: TextDark, fontSizeHalfPoints: 18, shadeHex: BadgeGray));
            _ = cell.AppendChild(numberParagraph);

            var imageParagraph = new Ooxml.Paragraph();
            var imageParagraphProperties = new Ooxml.ParagraphProperties();
            _ = imageParagraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = imageParagraph.AppendChild(imageParagraphProperties);
            if (!string.IsNullOrEmpty(optionFile))
            {
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, optionFile, httpClient, MaxImageOptionWidthPx, null);
                if (imageDrawing is not null)
                {
                    var imageRun = new Ooxml.Run();
                    _ = imageRun.AppendChild(imageDrawing);
                    _ = imageParagraph.AppendChild(imageRun);
                }
            }

            _ = cell.AppendChild(imageParagraph);
            return cell;
        }

        // ---- Answer key (template only -- see TestDto.CorrectOption) --------------------------------

        private const string AnswerKeyHeaderYellow = "FBE1A0";
        private const int AnswerKeyRowsPerBlock = 10;
        private const int AnswerKeyBlocksPerRow = 4;

        /// <summary>
        /// Appends an "Answer Key" page: one grid of mini answer-sheet tables (question number + a
        /// filled/empty square per option), 10 questions per block, up to 4 blocks per row -- the same
        /// layout as the reference template. This is a template only: <see cref="ExamInformationResponseDto
        /// .TestDto.CorrectOption"/> is always null today (Core doesn't return it yet), so every square
        /// renders empty/unmarked. Once Core adds that field and it's threaded through here, the marks
        /// start showing up with no layout changes needed -- only the data source changes.
        /// Deliberately one page's worth of blocks laid out top-to-bottom/left-to-right with no pagination
        /// of its own; for very large question counts this may run onto a second page via Word's own
        /// natural overflow (the blocks aren't marked non-splittable), which is acceptable for a template
        /// iteration.
        /// </summary>
        private static void AppendAnswerKeySection(Ooxml.Body body, List<ExamInformationResponseDto.TestDto> tests)
        {
            var pageBreakRun = new Ooxml.Run();
            _ = pageBreakRun.AppendChild(new Ooxml.Break { Type = Ooxml.BreakValues.Page });
            var pageBreakParagraph = new Ooxml.Paragraph();
            _ = pageBreakParagraph.AppendChild(pageBreakRun);
            _ = body.AppendChild(pageBreakParagraph);

            var heading = new Ooxml.Paragraph();
            _ = heading.AppendChild(CreateRun("Answer Key", bold: true, colorHex: TextDark, fontSizeHalfPoints: 32));
            _ = body.AppendChild(heading);
            _ = body.AppendChild(new Ooxml.Paragraph());

            var blockCount = (int)Math.Ceiling(tests.Count / (double)AnswerKeyRowsPerBlock);
            for (var blockStart = 0; blockStart < blockCount; blockStart += AnswerKeyBlocksPerRow)
            {
                var rowTable = new Ooxml.Table();
                var rowTableProperties = new Ooxml.TableProperties();
                _ = rowTableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
                _ = rowTableProperties.AppendChild(NoTableBorders());
                _ = rowTableProperties.AppendChild(FixedTableLayout());
                _ = rowTable.AppendChild(rowTableProperties);

                var blocksInThisRow = Math.Min(AnswerKeyBlocksPerRow, blockCount - blockStart);
                AppendTableGrid(rowTable, [.. Enumerable.Repeat(9026 / AnswerKeyBlocksPerRow, AnswerKeyBlocksPerRow)]);

                var row = new Ooxml.TableRow();
                for (var b = 0; b < blocksInThisRow; b++)
                {
                    var questionStart = (blockStart + b) * AnswerKeyRowsPerBlock;
                    var questionEnd = Math.Min(questionStart + AnswerKeyRowsPerBlock, tests.Count);
                    var cell = new Ooxml.TableCell();
                    var cellProperties = new Ooxml.TableCellProperties();
                    _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (9026 / AnswerKeyBlocksPerRow).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
                    _ = cellProperties.AppendChild(NoTableCellBorders());
                    _ = cell.AppendChild(cellProperties);
                    _ = cell.AppendChild(new Ooxml.Paragraph());
                    _ = cell.AppendChild(BuildAnswerKeyBlock(tests, questionStart, questionEnd));
                    _ = row.AppendChild(cell);
                }

                for (var b = blocksInThisRow; b < AnswerKeyBlocksPerRow; b++)
                {
                    var emptyCell = new Ooxml.TableCell();
                    var emptyCellProperties = new Ooxml.TableCellProperties();
                    _ = emptyCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (9026 / AnswerKeyBlocksPerRow).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
                    _ = emptyCellProperties.AppendChild(NoTableCellBorders());
                    _ = emptyCell.AppendChild(emptyCellProperties);
                    _ = emptyCell.AppendChild(new Ooxml.Paragraph());
                    _ = row.AppendChild(emptyCell);
                }

                _ = rowTable.AppendChild(row);
                _ = body.AppendChild(rowTable);
                _ = body.AppendChild(new Ooxml.Paragraph());
            }
        }

        /// <summary>One 10-question mini answer-sheet: a yellow "1 2 3 4" header row, then one row per
        /// question with its number and a filled (correct) or empty square per option.</summary>
        private static Ooxml.Table BuildAnswerKeyBlock(List<ExamInformationResponseDto.TestDto> tests, int questionStart, int questionEnd)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "2100", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = tableProperties.AppendChild(BorderedTableBorders(BorderLightGray));
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 500, 400, 400, 400, 400);

            var headerRow = new Ooxml.TableRow();
            _ = headerRow.AppendChild(AnswerKeyHeaderCell(string.Empty, "500"));
            foreach (var label in new[] { "1", "2", "3", "4" })
            {
                _ = headerRow.AppendChild(AnswerKeyHeaderCell(label, "400"));
            }

            _ = table.AppendChild(headerRow);

            for (var i = questionStart; i < questionEnd; i++)
            {
                var row = new Ooxml.TableRow();
                _ = row.AppendChild(AnswerKeyCell($"{i + 1}", RowGrayBg, bold: true, "500"));
                var correct = char.ToUpperInvariant(tests[i].CorrectOption ?? ' ');
                foreach (var optionLetter in new[] { 'A', 'B', 'C', 'D' })
                {
                    var mark = correct == optionLetter ? "■" : "□";
                    _ = row.AppendChild(AnswerKeyCell(mark, "FFFFFF", bold: false, "400"));
                }

                _ = table.AppendChild(row);
            }

            return table;
        }

        private static Ooxml.TableCell AnswerKeyHeaderCell(string text, string widthDxa)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            if (text.Length > 0)
            {
                _ = paragraph.AppendChild(CreateRun(text, bold: true, colorHex: TextDark, fontSizeHalfPoints: 18));
            }

            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = AnswerKeyHeaderYellow });
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static Ooxml.TableCell AnswerKeyCell(string text, string fillHex, bool bold, string widthDxa)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(CreateRun(text, bold: bold, colorHex: TextDark, fontSizeHalfPoints: 18));

            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = fillHex });
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        // ---- Small shared builders ------------------------------------------------------------------

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

        private static Ooxml.Paragraph WrapInParagraph(Ooxml.Run run, Ooxml.JustificationValues alignment)
        {
            var paragraph = new Ooxml.Paragraph();
            var properties = new Ooxml.ParagraphProperties();
            _ = properties.AppendChild(new Ooxml.Justification { Val = alignment });
            _ = paragraph.AppendChild(properties);
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

        internal static async Task<Ooxml.Drawing?> EmbedImageFromSourceAsync<TPart>(
            TPart mainPart, string src, Lazy<HttpClient>? httpClient, int? fixedWidthPx, int? fixedHeightPx)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
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

            return EmbedImageBytes(mainPart, bytes, fixedWidthPx, fixedHeightPx);
        }

        internal static Ooxml.Drawing? EmbedImageBytes<TPart>(TPart mainPart, byte[] bytes, int? fixedWidthPx, int? fixedHeightPx)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            var built = BuildImageGraphic(mainPart, bytes, fixedWidthPx, fixedHeightPx);
            if (built is null)
            {
                return null;
            }

            var (graphic, widthEmu, heightEmu, drawingId) = built.Value;

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

        private static (A.Graphic Graphic, long WidthEmu, long HeightEmu, uint DrawingId)? BuildImageGraphic<TPart>(
            TPart mainPart, byte[] bytes, int? fixedWidthPx, int? fixedHeightPx)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                return null;
            }

            int widthPx;
            int heightPx;
            if (fixedWidthPx is not null && fixedHeightPx is not null)
            {
                // Both given -- an explicit override (e.g. forcing a square icon/QR to a fixed box), aspect
                // ratio is the caller's problem, not ours.
                widthPx = fixedWidthPx.Value;
                heightPx = fixedHeightPx.Value;
            }
            else if (fixedWidthPx is not null)
            {
                // Width-only: scale height to match, or a non-square source (almost everything real) comes
                // out stretched -- this exact bug showed up as a question's side image rendering squashed
                // to the requested width but full native height, spilling down the whole page.
                widthPx = fixedWidthPx.Value;
                heightPx = (int)Math.Round(bitmap.Height * (widthPx / (double)bitmap.Width));
            }
            else if (fixedHeightPx is not null)
            {
                heightPx = fixedHeightPx.Value;
                widthPx = (int)Math.Round(bitmap.Width * (heightPx / (double)bitmap.Height));
            }
            else
            {
                widthPx = bitmap.Width;
                heightPx = bitmap.Height;
                if (widthPx > MaxImageWidthPx)
                {
                    heightPx = (int)Math.Round(heightPx * (MaxImageWidthPx / (double)widthPx));
                    widthPx = MaxImageWidthPx;
                }
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

            return (graphic, widthEmu, heightEmu, drawingId);
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
        private static async Task AddPageHeaderAndFooterAsync(
            MainDocumentPart mainPart, ExamInformationResponseDto.ExamDto? exam, string? watermarkText, HeaderBrandAssets brandAssets)
        {
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            var header = new Ooxml.Header();
#pragma warning disable S1075 // spec-mandated OOXML namespace URIs, not configurable endpoints
            header.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            header.AddNamespaceDeclaration("wps", "http://schemas.microsoft.com/office/word/2010/wordprocessingShape");
            header.AddNamespaceDeclaration("wp", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
            header.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

            // Found by bisecting variants against Google Docs' importer (2026-09-20): with the wps shapes in
            // mc:AlternateContent but these root declarations missing, Google rejects the whole file ("File could
            // not open"); declaring them and marking wps ignorable - exactly what Word itself writes - makes it
            // open (background skipped), while Word/LibreOffice still render the shapes. The custom Bezier
            // geometry, and a VML fallback, were both ruled out as the cause.
            header.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = "wps" };
#pragma warning restore S1075
            _ = header.AppendChild(BuildHeaderBackgroundParagraph());
            _ = header.AppendChild(await BuildHeaderRowAsync(exam, headerPart, brandAssets));
            if (!string.IsNullOrEmpty(watermarkText))
            {
                _ = header.AppendChild(BuildWatermarkParagraph(watermarkText));
            }

            headerPart.Header = header;
            headerPart.Header.Save();

            var footerPart = mainPart.AddNewPart<FooterPart>();
            var footer = new Ooxml.Footer();
            _ = footer.AppendChild(BuildFooterTable(footerPart, brandAssets));
            _ = footer.AppendChild(BuildFooterWaveParagraph(footerPart, brandAssets));
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

        /// <summary>
        /// Matches the reference template's exact footer exactly: "{PAGE} / {NUMPAGES}" on the left, a real
        /// globe icon (exam-footer-globe.png) plus "www.gamatrain.com" centered -- no "Page"/"of" words and
        /// no copyright text, both of which the earlier revision had invented without a reference to match.
        /// </summary>
        private static Ooxml.Table BuildFooterTable<TPart>(TPart footerPart, HeaderBrandAssets brandAssets)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = tableProperties.AppendChild(NoTableBorders());
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 3009, 3009, 3008);

            var row = new Ooxml.TableRow();

            var pageParagraph = new Ooxml.Paragraph();
            _ = pageParagraph.AppendChild(BuildPageField("PAGE"));
            _ = pageParagraph.AppendChild(CreateRun(" / ", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16));
            _ = pageParagraph.AppendChild(BuildPageField("NUMPAGES"));
            _ = row.AppendChild(BuildBorderlessCell(pageParagraph, Ooxml.JustificationValues.Left, "1667"));

            var globeDrawing = EmbedImageBytes(footerPart, brandAssets.FooterGlobe, 14, 14);
            var siteParagraph = new Ooxml.Paragraph();
            if (globeDrawing is not null)
            {
                var globeRun = new Ooxml.Run();
                _ = globeRun.AppendChild(globeDrawing);
                _ = siteParagraph.AppendChild(globeRun);
                _ = siteParagraph.AppendChild(CreateRun(" ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 16));
            }

            _ = siteParagraph.AppendChild(CreateRun("www.gamatrain.com", bold: true, colorHex: TextDark, fontSizeHalfPoints: 16));
            _ = row.AppendChild(BuildBorderlessCell(siteParagraph, Ooxml.JustificationValues.Center, "1667"));

            _ = row.AppendChild(BuildBorderlessCell(new Ooxml.Paragraph(), Ooxml.JustificationValues.Right, "1666"));

            _ = table.AppendChild(row);
            return table;
        }

        /// <summary>
        /// The decorative light-grey shape (exam-footer-wave.png, a real asset from the reference template,
        /// not a hand-drawn shape) centered just below the footer text on every page.
        /// </summary>
        private static Ooxml.Paragraph BuildFooterWaveParagraph<TPart>(TPart footerPart, HeaderBrandAssets brandAssets)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);

            var waveDrawing = EmbedImageBytes(footerPart, brandAssets.FooterWave, 60, 20);
            if (waveDrawing is not null)
            {
                var run = new Ooxml.Run();
                _ = run.AppendChild(waveDrawing);
                _ = paragraph.AppendChild(run);
            }

            return paragraph;
        }

        private static Ooxml.SimpleField BuildPageField(string instruction)
        {
            var field = new Ooxml.SimpleField { Instruction = instruction };
            _ = field.AppendChild(CreateRun("1", bold: false, colorHex: TextMuted, fontSizeHalfPoints: 16));
            return field;
        }

        private static Ooxml.TableCell BuildBorderlessCell(Ooxml.Paragraph paragraph, Ooxml.JustificationValues alignment, string widthPct)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthPct, Type = Ooxml.TableWidthUnitValues.Pct });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cell.AppendChild(cellProperties);

            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = alignment });
            _ = paragraph.InsertAt(paragraphProperties, 0);
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
