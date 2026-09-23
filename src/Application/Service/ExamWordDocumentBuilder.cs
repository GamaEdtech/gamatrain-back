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
        // Page geometry -- single source of truth for BuildAsync's own w:pgMar plus BuildHeaderRowAsync/
        // BuildHeaderBackgroundParagraph's content-width math, which both need to know the real usable
        // width (page width minus left/right margins) to size/position the header table and its decorative
        // background. Values matched exactly to the reference template's own real w:pgMar, measured
        // directly from its document.xml, not guessed. A4 in dxa: 11906 x 16838. The outsized top margin
        // gives the decorative header room to clear before body content starts; Footer=0 lets the footer's
        // own content sit right at the page's bottom margin with nothing held back for it. Before this was
        // pulled out to one place, BuildHeaderRowAsync/BuildHeaderBackgroundParagraph each hardcoded their
        // own content-width constant derived from an *earlier* (narrower) margin -- fixed 2026-09-22 after
        // the margins above changed and those two stayed stale, silently narrowing the header/background
        // relative to the page's real usable width.
        private const int PageWidthDxa = 11906;
        private const int PageHeightDxa = 16838;
        private const int PageMarginTopDxa = 2977;
        private const int PageMarginRightDxa = 720;
        private const int PageMarginBottomDxa = 720;
        private const int PageMarginLeftDxa = 720;
        private const int PageMarginHeaderDxa = 720;
        private const int PageMarginFooterDxa = 0;
        private const int PageContentWidthDxa = PageWidthDxa - PageMarginLeftDxa - PageMarginRightDxa;

        private const long EmuPerPixel = 9525; // 96dpi CSS px -> EMU
        private const int MaxImageWidthPx = 500;

        /// <summary>Cap for a question's own shared image attached to any text options layout (see
        /// LoadSharedQuestionImageAsync) -- that merged column is ~2300dxa (~1.6in) wide, well under
        /// MaxImageWidthPx.</summary>
        private const int MaxQuestionSideImageWidthPx = 150;

        /// <summary>Cap for one thumbnail in an all-image options row (see RenderImageOptionsHorizontalAsync)
        /// -- each content cell is 1600dxa (~1.1in) wide, narrower than the badge+content pair's own margins
        /// leave room for at native size.</summary>
        private const int MaxImageOptionWidthPx = 90;
        private const string BorderLightGray = "C5D0DB";
        private const string RowGrayBg = "F4F7FB";
        private const string TextDark = "172033";
        private const string TextMuted = "5B6777";

        // Light-grey badge style (question number / option number) -- matches the reference template's own
        // real badge fill (#EDEDED), measured directly from its document.xml, 2026-09-22.
        private const string BadgeGray = "EDEDED";

        // A badge's grey chip is a nested auto-width table (see BuildNumberBadgeChip), not the outer grid
        // cell itself, so it can carry real `w:tcMar` padding on all 4 sides -- a run-level `w:shd` (the
        // prior approach) has no padding concept and paints tight to the glyph's own bounding box.
        private const int BadgeChipHorizontalPaddingDxa = 80;
        private const int BadgeChipVerticalPaddingDxa = 40;

        /// <summary>
        /// The dark-blue rule drawn under every question (see <see cref="AppendSeparatorRows"/>) -- matches
        /// the reference template's own real separator color (#002060, `single` border, size 8 = 1pt),
        /// measured directly from its document.xml, 2026-09-22 (previously an approximate brand navy).
        /// </summary>
        private const string SeparatorNavy = "002060";

        /// <summary>
        /// An option's plain-text length (HTML stripped) at or below which <see cref="QuestionLayoutType.TextHorizontal"/>
        /// applies (four options fit side by side in one row) -- above this but at/below
        /// <see cref="LongOptionTextThreshold"/>, <see cref="QuestionLayoutType.Text2x2"/> is used instead.
        /// </summary>
        private const int ShortOptionTextThreshold = 12;

        /// <summary>
        /// An option's plain-text length (HTML stripped) above which the options layout switches to a
        /// single stacked column (<see cref="QuestionLayoutType.TextVertical"/>) -- matches the reference
        /// template, where short options ("ampere") sit multiple-per-row but longer ones ("concerned about
        /// the level...") each get a full-width row instead of being squeezed into a narrower cell.
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
                    // One shared table for every question -- matches the reference template's own real
                    // structure exactly (confirmed 2026-09-22 by reading its document.xml cell-by-cell: a
                    // single 31-row table for 5 sample questions, not one table per question). Each question
                    // is a group of rows in this same table (header rows, then its own option rows, then two
                    // thin spacer rows -- one carrying the navy separator border -- before the next question's
                    // rows begin), sharing one tblGrid throughout. This is a real structural difference from
                    // an earlier revision, which gave every question its own separate top-level table plus a
                    // nested table for its options; that revision's own doc comment claimed this was
                    // "verified against a genuine Word document" to be necessary, which doesn't hold up
                    // against the reference's actual file.
                    var questionsTable = BuildSharedQuestionTable();
                    for (var i = 0; i < data.Tests.Count; i++)
                    {
                        await AppendQuestionAsync(questionsTable, data.Tests[i], i, data.Tests.Count, mainPart, httpClient);
                    }

                    _ = body.AppendChild(questionsTable);
                    AppendAnswerKeySection(body, data.Tests);
                }

                RemoveDefaultTableStyle(body);
                PreventRowsSplittingAcrossPages(body);

                _ = body.AppendChild(new Ooxml.SectionProperties());
                var sectionProperties = body.Elements<Ooxml.SectionProperties>().Single();
                _ = sectionProperties.AppendChild(new Ooxml.PageSize { Width = PageWidthDxa, Height = PageHeightDxa });
                _ = sectionProperties.AppendChild(new Ooxml.PageMargin
                {
                    Top = PageMarginTopDxa,
                    Right = PageMarginRightDxa,
                    Bottom = PageMarginBottomDxa,
                    Left = PageMarginLeftDxa,
                    Header = PageMarginHeaderDxa,
                    Footer = PageMarginFooterDxa,
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
            const int contentWidthDxa = PageContentWidthDxa;

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

            // Without an explicit minimum, this row's real height is whatever the 52px-tall wordmark image
            // plus default cell margins naturally add up to (~950-980dxa) -- a bit tight against the panel's
            // own diagonal-cut shape behind it. AtLeast (not Exact) still lets the row grow further if a
            // future asset/content needs more room; it never gets shorter than this floor.
            var brandRowProperties = new Ooxml.TableRowProperties();
            _ = brandRowProperties.AppendChild(new Ooxml.TableRowHeight { Val = 900, HeightType = Ooxml.HeightRuleValues.AtLeast });
            _ = brandRow.AppendChild(brandRowProperties);

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
            const int contentWidthDxa = PageContentWidthDxa; // matches BuildHeaderRowAsync's own table width
            const int referenceWidth = 547;
            const int referenceHeight = 104;
            const long dxaToEmu = 635;

            var widthEmu = contentWidthDxa * dxaToEmu;
            var heightEmu = (long)Math.Round(widthEmu * ((double)referenceHeight / referenceWidth));
            var leftOffsetEmu = PageMarginLeftDxa * dxaToEmu;
            var topOffsetEmu = PageMarginHeaderDxa * dxaToEmu;

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

            // Shape 5: plain light-grey filler band (#F2F4F7, same fill as shapes 3/4), continuing seamlessly
            // where shape 4 ends down to just above the body's top margin (2977dxa, see BuildAsync's
            // PageMargin -- matches Temp.docx's own measured w:pgMar). Not part of the reference's own
            // image4.svg: the header table's real content (brand/title/metadata rows) is much shorter than
            // that margin, so without this the page showed a large blank gap between the header and the
            // first question -- a plain rectangle (no rounded corners to match) reads as a continuation of
            // shape 4's band, not a separate element, so the seam is invisible.
            var filler4BottomDxa = ((PageMarginHeaderDxa * dxaToEmu) + heightEmu) / dxaToEmu;
            const int fillerBottomBufferDxa = 100;
            var fillerHeightDxa = PageMarginTopDxa - fillerBottomBufferDxa - filler4BottomDxa;
            if (fillerHeightDxa > 0)
            {
                const int fillerReferenceHeight = 100;
                _ = run.AppendChild(BuildBackgroundShapeDrawing(
                    "F2F4F7", widthEmu, fillerHeightDxa * dxaToEmu, leftOffsetEmu, (PageMarginHeaderDxa * dxaToEmu) + heightEmu,
                    referenceWidth, fillerReferenceHeight,
                    [
                        PathCommand.Move(0, 0),
                        PathCommand.Line(547, 0),
                        PathCommand.Line(547, 100),
                        PathCommand.Line(0, 100),
                        PathCommand.Close(),
                    ]));
            }

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
        //
        // All questions share ONE table (see BuildSharedQuestionTable/AppendQuestionAsync) with one shared
        // column grid (QuestionNumberColumnDxa + ContentFineColumnCount equal-width fine columns) -- matches
        // the reference template's own real structure exactly. Every layout expresses itself purely via
        // `w:gridSpan` over that same fine grid (see the Build*OptionRowsAsync methods), the same technique
        // the reference itself uses (its own real grid has 11 columns of uneven, hand-tuned widths for its
        // 5 sample questions; this one uses 16 equal-width columns instead, general enough for arbitrary
        // real content rather than one fixed sample). ClassifyLayout maps a TestDto onto one of four
        // QuestionLayoutType values (the normalized type a real Core `answer_view_type` field carries), and
        // BuildOptionRowsAsync dispatches to one dedicated Build*OptionRowsAsync method per type. Every
        // method shares the same small set of cell builders (BuildOptionBadgeCell, BuildOptionContentCellAsync,
        // BuildImageMergeCell*) so formatting stays identical across layouts.

        /// <summary>
        /// The options-arrangement layouts this builder can render, normalized independently of Core's own
        /// field names (see <see cref="ClassifyLayout"/>). A question's own shared image (<see
        /// cref="ExamInformationResponseDto.TestDto.QuestionFile"/>) is an orthogonal concern layered onto
        /// whichever of the three text arrangements applies -- see <see cref="BuildOptionRowsAsync"/> --
        /// not a fourth/fifth member here, because real Core data (confirmed live against exam 1061) shows a
        /// question image commonly accompanies any of the three, not just the stacked one.
        /// </summary>
        internal enum QuestionLayoutType
        {
            /// <summary>All four options in one row: badge+text, badge+text, badge+text, badge+text.</summary>
            TextHorizontal,

            /// <summary>Options as a 2x2 grid (1|2 on one row, 3|4 on the next).</summary>
            Text2x2,

            /// <summary>Options stacked one per row.</summary>
            TextVertical,

            /// <summary>All four options are images, laid out side by side in one row.</summary>
            ImageOptionsHorizontal,
        }

        /// <summary>One option slot (1-4), independently text-or-image per <see cref="ExamInformationResponseDto.TestDto"/>'s paired OptionX/OptionXFile fields.</summary>
        private readonly record struct OptionModel(string Number, string? Html, string? File);

        private static OptionModel[] GetOptionModels(ExamInformationResponseDto.TestDto test) =>
        [
            new("1", test.OptionA, test.OptionAFile),
            new("2", test.OptionB, test.OptionBFile),
            new("3", test.OptionC, test.OptionCFile),
            new("4", test.OptionD, test.OptionDFile),
        ];

        /// <summary>
        /// Normalizes a question onto one of the four <see cref="QuestionLayoutType"/> values, preferring
        /// Core's own real fields (confirmed live 2026-09-22 against exams 831/832/1061/2037, 64 real
        /// questions total) over guessing from content shape:
        /// <list type="bullet">
        /// <item><see cref="ExamInformationResponseDto.TestDto.TestImageAnswers"/> ("testImgAnswers") is
        /// Core's own "all four options are images" flag -- authoritative over checking that every option's
        /// text is blank.</item>
        /// <item><see cref="ExamInformationResponseDto.TestDto.AnswerViewType"/> ("answer_view_type") was
        /// only ever seen as "1"/"2"/"4" across the whole sample; cross-referenced against those questions'
        /// real option text lengths, the values line up exactly with the options column count -- "4" across
        /// one row, "2" per row, "1" per row (stacked). No value implying an image-specific arrangement was
        /// ever observed.</item>
        /// </list>
        /// Falls back to the old text-length heuristic only when <c>AnswerViewType</c> is null/unrecognized
        /// (an older Core deployment, or a value this sample never hit) -- so a genuinely new Core value
        /// degrades to a reasonable guess instead of breaking.
        /// </summary>
        private static QuestionLayoutType ClassifyLayout(ExamInformationResponseDto.TestDto test, OptionModel[] options)
        {
            var isImageOptions = test.TestImageAnswers
                || options.All(o => !string.IsNullOrEmpty(o.File) && PlainTextLength(o.Html) == 0);
            if (isImageOptions)
            {
                return QuestionLayoutType.ImageOptionsHorizontal;
            }

            var mappedFromCore = test.AnswerViewType switch
            {
                "4" => QuestionLayoutType.TextHorizontal,
                "2" => QuestionLayoutType.Text2x2,
                "1" => QuestionLayoutType.TextVertical,
                _ => (QuestionLayoutType?)null,
            };

            if (mappedFromCore is not null)
            {
                return mappedFromCore.Value;
            }

            var maxOptionLength = options.Max(o => PlainTextLength(o.Html));
            return maxOptionLength switch
            {
                _ when maxOptionLength > LongOptionTextThreshold => QuestionLayoutType.TextVertical,
                _ when maxOptionLength <= ShortOptionTextThreshold => QuestionLayoutType.TextHorizontal,
                _ => QuestionLayoutType.Text2x2,
            };
        }

        /// <summary>Question-number column width (dxa) -- matches the reference template's own real first
        /// column (measured directly from its document.xml, 2026-09-22).</summary>
        private const int QuestionNumberColumnDxa = 658;

        /// <summary>Width (dxa) of each of the <see cref="ContentFineColumnCount"/> equal fine columns the
        /// content area (right of the question-number column) is divided into -- every layout expresses
        /// itself as a `w:gridSpan` combination of these, so <c>QuestionNumberColumnDxa + ContentFineColumnCount
        /// * ContentFineColumnDxa</c> must equal <see cref="PageContentWidthDxa"/> exactly.</summary>
        private const int ContentFineColumnDxa = 613;

        /// <summary>Fine columns in the content area. Chosen as the smallest count that lets every layout's
        /// badge/option/image split land on whole-column boundaries: <see cref="QuestionLayoutType.TextHorizontal"/>/
        /// <see cref="QuestionLayoutType.ImageOptionsHorizontal"/> need 4 (badge+content) pairs (8 columns);
        /// with a shared image attached (see <see cref="LoadSharedQuestionImageAsync"/>), the image itself
        /// takes the last <see cref="ImageFineColumnSpan"/> columns and every layout's badge/option split
        /// still divides the remaining 12 evenly.</summary>
        private const int ContentFineColumnCount = 16;

        /// <summary>How many of the <see cref="ContentFineColumnCount"/> fine columns a shared question image
        /// (see <see cref="LoadSharedQuestionImageAsync"/>) occupies, always the last ones in the row.</summary>
        private const int ImageFineColumnSpan = 4;

        /// <summary>
        /// One shared table for the whole exam, matching the reference template's own real structure exactly
        /// (confirmed 2026-09-22 by reading its document.xml cell-by-cell: one continuous table for every
        /// question, not a separate table per question) -- <see cref="AppendQuestionAsync"/> appends each
        /// question's rows directly onto this same table. No table-level borders (the reference has none
        /// either): the only visible rule anywhere is the navy line <see cref="AppendSeparatorRows"/> draws
        /// as one row's own bottom border.
        /// </summary>
        private static Ooxml.Table BuildSharedQuestionTable()
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "5000", Type = Ooxml.TableWidthUnitValues.Pct });
            _ = tableProperties.AppendChild(NoTableBorders());
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);

            var columnWidths = new int[1 + ContentFineColumnCount];
            columnWidths[0] = QuestionNumberColumnDxa;
            for (var i = 1; i < columnWidths.Length; i++)
            {
                columnWidths[i] = ContentFineColumnDxa;
            }

            AppendTableGrid(table, columnWidths);
            return table;
        }

        /// <summary>
        /// Appends one question's full row group to the shared table: two question-number/text rows (see
        /// <see cref="AppendQuestionHeaderRowsAsync"/>), then its option rows (or, for a descriptive question
        /// with its own image, one centered-image row), then two thin spacer rows -- the first carrying the
        /// navy separator border, the second blank padding beneath it -- matching the reference's own
        /// between-question spacing exactly. The very last question only gets the bordered spacer row, no
        /// trailing blank one (matches the reference, which ends its table right after the last question's
        /// separator).
        /// </summary>
        private static async Task AppendQuestionAsync(
            Ooxml.Table table, ExamInformationResponseDto.TestDto test, int index, int totalCount, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            await AppendQuestionHeaderRowsAsync(table, test, index + 1, mainPart, httpClient);

            if (test.HasOptions)
            {
                foreach (var row in await BuildOptionRowsAsync(test, mainPart, httpClient))
                {
                    _ = table.AppendChild(row);
                }
            }
            else if (!string.IsNullOrEmpty(test.QuestionFile))
            {
                // Descriptive question (no options at all, see HasOptions) with its own image: no options
                // layout to share it with, so it simply sits centered in its own row.
                _ = table.AppendChild(await BuildDescriptiveImageRowAsync(test.QuestionFile, mainPart, httpClient));
            }

            AppendSeparatorRows(table, includeTrailingBlankRow: index < totalCount - 1);
        }

        /// <summary>
        /// The two rows every question starts with: a question-number badge (shown only on the first row;
        /// the second row's badge cell is left blank -- matches the reference, which does not vertically
        /// merge the number column at all, just repeats its shading) beside a question-text cell that IS
        /// vertically merged (`w:vMerge`) across both rows, so a question text long enough to wrap grows into
        /// the second row naturally instead of being clipped to a fixed one-row height.
        /// </summary>
        private static async Task AppendQuestionHeaderRowsAsync(
            Ooxml.Table table, ExamInformationResponseDto.TestDto test, int number, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var questionFormat = new ExamWordRichText.RunFormat(true, false, false, false, false, TextDark, 22);
            var questionParagraphs = await ExamWordRichText.ParseToParagraphsAsync(test.Question, mainPart, httpClient, questionFormat);

            var contentWidthDxa = (ContentFineColumnDxa * ContentFineColumnCount).ToString(CultureInfo.InvariantCulture);

            var startCell = new Ooxml.TableCell();
            var startCellProperties = new Ooxml.TableCellProperties();
            _ = startCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = contentWidthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = startCellProperties.AppendChild(new Ooxml.GridSpan { Val = ContentFineColumnCount });
            _ = startCellProperties.AppendChild(new Ooxml.VerticalMerge { Val = Ooxml.MergedCellValues.Restart });
            _ = startCellProperties.AppendChild(NoTableCellBorders());
            _ = startCell.AppendChild(startCellProperties);
            foreach (var paragraph in questionParagraphs)
            {
                _ = startCell.AppendChild(paragraph);
            }

            var startRow = new Ooxml.TableRow();
            _ = startRow.AppendChild(BuildQuestionNumberCell(number, showNumber: true));
            _ = startRow.AppendChild(startCell);
            _ = table.AppendChild(startRow);

            var continueCell = new Ooxml.TableCell();
            var continueCellProperties = new Ooxml.TableCellProperties();
            _ = continueCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = contentWidthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = continueCellProperties.AppendChild(new Ooxml.GridSpan { Val = ContentFineColumnCount });
            _ = continueCellProperties.AppendChild(new Ooxml.VerticalMerge { Val = Ooxml.MergedCellValues.Continue });
            _ = continueCellProperties.AppendChild(NoTableCellBorders());
            _ = continueCell.AppendChild(continueCellProperties);
            _ = continueCell.AppendChild(new Ooxml.Paragraph());

            var continueRow = new Ooxml.TableRow();
            _ = continueRow.AppendChild(BuildQuestionNumberCell(number, showNumber: false));
            _ = continueRow.AppendChild(continueCell);
            _ = table.AppendChild(continueRow);
        }

        /// <summary>The question-number column's own cell -- a light-grey badge chip (see
        /// <see cref="BuildNumberBadgeChip"/>) throughout the question's row group, but the digit itself only
        /// drawn on the block's first row (<paramref name="showNumber"/> false everywhere else: the header's
        /// continuation row, every option row, matching the reference, which never vertically merges this
        /// column, just repeats a blank cell beneath it -- no chip is drawn there since there's no digit).</summary>
        private static Ooxml.TableCell BuildQuestionNumberCell(int number, bool showNumber)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = QuestionNumberColumnDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Top });
            _ = cell.AppendChild(cellProperties);

            if (showNumber)
            {
                _ = cell.AppendChild(BuildNumberBadgeChip(number.ToString(CultureInfo.InvariantCulture), fontSizeHalfPoints: 22));
            }

            // A table cell's content must end with a paragraph, not a table (see the schema-order note on
            // BuildQuestionTableAsync's history elsewhere in this file) -- always appended, whether or not
            // the chip table above was added, so a cell with no number still has valid, empty content.
            _ = cell.AppendChild(new Ooxml.Paragraph());
            return cell;
        }

        /// <summary>
        /// The two thin rows between one question's rows and the next: the first carries the navy separator
        /// as its own bottom border (a real table border, not a drawn shape), the second is blank padding
        /// beneath it -- both use <see cref="BuildThinSpacerParagraph"/> so they collapse close to zero
        /// height instead of Word's default line height. Matches the reference's own real separator rows
        /// exactly (measured directly from its document.xml, 2026-09-22): border `single`, size 8 (1pt),
        /// color <see cref="SeparatorNavy"/>. The last question in the exam only gets the bordered row.
        /// </summary>
        private static void AppendSeparatorRows(Ooxml.Table table, bool includeTrailingBlankRow)
        {
            const int totalColumns = 1 + ContentFineColumnCount;
            var totalWidthDxa = (QuestionNumberColumnDxa + (ContentFineColumnDxa * ContentFineColumnCount)).ToString(CultureInfo.InvariantCulture);

            var borderedCellProperties = new Ooxml.TableCellProperties();
            _ = borderedCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = totalWidthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = borderedCellProperties.AppendChild(new Ooxml.GridSpan { Val = totalColumns });
            var borders = new Ooxml.TableCellBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = SeparatorNavy, Size = 8 });
            _ = borders.AppendChild(new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borderedCellProperties.AppendChild(borders);
            var borderedCell = new Ooxml.TableCell();
            _ = borderedCell.AppendChild(borderedCellProperties);
            _ = borderedCell.AppendChild(BuildThinSpacerParagraph());

            var borderedRow = new Ooxml.TableRow();
            _ = borderedRow.AppendChild(borderedCell);
            _ = table.AppendChild(borderedRow);

            if (!includeTrailingBlankRow)
            {
                return;
            }

            var blankCellProperties = new Ooxml.TableCellProperties();
            _ = blankCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = totalWidthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = blankCellProperties.AppendChild(new Ooxml.GridSpan { Val = totalColumns });
            _ = blankCellProperties.AppendChild(NoTableCellBorders());
            var blankCell = new Ooxml.TableCell();
            _ = blankCell.AppendChild(blankCellProperties);
            _ = blankCell.AppendChild(BuildThinSpacerParagraph());

            var blankRow = new Ooxml.TableRow();
            _ = blankRow.AppendChild(blankCell);
            _ = table.AppendChild(blankRow);
        }

        /// <summary>Empty paragraph with a near-zero mark run font size, so a row containing only this
        /// collapses close to zero height instead of Word's default (~11pt) line height -- same technique
        /// <see cref="BuildHeaderBackgroundParagraph"/> uses for the same reason.</summary>
        private static Ooxml.Paragraph BuildThinSpacerParagraph()
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = Ooxml.LineSpacingRuleValues.Auto });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);
            return paragraph;
        }

        /// <summary>A descriptive question's (no options, see HasOptions) own image, centered in a single
        /// full-width row -- no options layout exists to share it with.</summary>
        private static async Task<Ooxml.TableRow> BuildDescriptiveImageRowAsync(string questionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var imageDrawing = await EmbedImageFromSourceAsync(mainPart, questionFile, httpClient, null, null);

            var contentCellProperties = new Ooxml.TableCellProperties();
            _ = contentCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (ContentFineColumnDxa * ContentFineColumnCount).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = contentCellProperties.AppendChild(new Ooxml.GridSpan { Val = ContentFineColumnCount });
            _ = contentCellProperties.AppendChild(NoTableCellBorders());
            var contentCell = new Ooxml.TableCell();
            _ = contentCell.AppendChild(contentCellProperties);

            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            if (imageDrawing is not null)
            {
                var run = new Ooxml.Run();
                _ = run.AppendChild(imageDrawing);
                _ = paragraph.AppendChild(run);
            }

            _ = contentCell.AppendChild(paragraph);

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
            _ = row.AppendChild(contentCell);
            return row;
        }

        /// <summary>
        /// Each option is TWO cells -- a narrow shaded badge cell (letter) plus its own content cell -- not
        /// one cell with a colored run and a tab character faking a badge. Dispatches on
        /// <see cref="ClassifyLayout"/>'s result; every branch below returns the option row(s) to append
        /// directly onto the shared question table, expressing its layout purely via `w:gridSpan` over the
        /// table's own shared fine-column grid (see <see cref="ContentFineColumnCount"/>) -- no independent
        /// nested table, matching the reference template's own real structure.
        /// </summary>
        private static Task<List<Ooxml.TableRow>> BuildOptionRowsAsync(
            ExamInformationResponseDto.TestDto test, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var options = GetOptionModels(test);
            var layout = ClassifyLayout(test, options);

            // The image-options row has no room/need for a second, separate shared-question image -- the
            // four option cells already are the images. Every other layout accepts one.
            var questionImageFile = layout == QuestionLayoutType.ImageOptionsHorizontal ? null : test.QuestionFile;

            return layout switch
            {
                QuestionLayoutType.ImageOptionsHorizontal => BuildImageOptionsHorizontalRowsAsync(options, mainPart, httpClient),
                QuestionLayoutType.TextVertical => BuildTextVerticalOptionRowsAsync(options, questionImageFile, mainPart, httpClient),
                QuestionLayoutType.TextHorizontal => BuildTextHorizontalOptionRowsAsync(options, questionImageFile, mainPart, httpClient),
                _ => BuildText2x2OptionRowsAsync(options, questionImageFile, mainPart, httpClient),
            };
        }

        /// <summary>
        /// Loads a question's own shared image once (width-only cap, height left to float with the source
        /// aspect ratio -- same technique <c>EmbedImageFromSourceAsync</c> uses everywhere else in this
        /// file), for whichever text layout is about to attach it as a merged column. Returns
        /// <see langword="null"/> when there is no image, which every caller treats as "render the plain,
        /// image-less variant of this layout".
        /// </summary>
        private static Task<Ooxml.Drawing?> LoadSharedQuestionImageAsync(string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient) =>
            string.IsNullOrEmpty(questionImageFile)
                ? Task.FromResult<Ooxml.Drawing?>(null)
                : EmbedImageFromSourceAsync(mainPart, questionImageFile, httpClient, MaxQuestionSideImageWidthPx, null);

        /// <summary>
        /// QUESTION TYPE 1: all four options in one row, each a narrow badge cell plus its own answer cell --
        /// 4 (badge=1 column, option=3 columns) pairs span all 16 fine columns. When the question carries its
        /// own shared image (confirmed live: real Core data commonly pairs one with any options arrangement,
        /// not just the stacked layout), each pair's option cell shrinks to 2 columns, freeing the last
        /// <see cref="ImageFineColumnSpan"/> columns for the image -- a single row needs no `w:vMerge` for it,
        /// unlike the 2x2/vertical variants below.
        /// </summary>
        private static async Task<List<Ooxml.TableRow>> BuildTextHorizontalOptionRowsAsync(
            OptionModel[] options, string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var imageDrawing = await LoadSharedQuestionImageAsync(questionImageFile, mainPart, httpClient);
            var optionSpan = imageDrawing is null ? 3 : 2;

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
            foreach (var option in options)
            {
                _ = row.AppendChild(BuildOptionBadgeCell(option.Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(option.Html, option.File, mainPart, httpClient, optionSpan));
            }

            if (imageDrawing is not null)
            {
                _ = row.AppendChild(BuildImageMergeCellStart(imageDrawing, ImageFineColumnSpan));
            }

            return [row];
        }

        /// <summary>
        /// QUESTION TYPE 2: options as a 2x2 grid (1|2 on one row, 3|4 on the next) -- 2 (badge=1 column,
        /// option=7 columns) pairs per row span all 16 fine columns. A shared question image shrinks each
        /// option cell to 5 columns and becomes a third column merged (`w:vMerge`) across both option rows.
        /// </summary>
        private static async Task<List<Ooxml.TableRow>> BuildText2x2OptionRowsAsync(
            OptionModel[] options, string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var imageDrawing = await LoadSharedQuestionImageAsync(questionImageFile, mainPart, httpClient);
            var optionSpan = imageDrawing is null ? 7 : 5;
            var rows = new List<Ooxml.TableRow>();

            for (var i = 0; i < options.Length; i += 2)
            {
                var row = new Ooxml.TableRow();
                _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
                _ = row.AppendChild(BuildOptionBadgeCell(options[i].Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(options[i].Html, options[i].File, mainPart, httpClient, optionSpan));
                _ = row.AppendChild(BuildOptionBadgeCell(options[i + 1].Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(options[i + 1].Html, options[i + 1].File, mainPart, httpClient, optionSpan));
                if (imageDrawing is not null)
                {
                    _ = row.AppendChild(i == 0 ? BuildImageMergeCellStart(imageDrawing, ImageFineColumnSpan) : BuildImageMergeCellContinue(ImageFineColumnSpan));
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// QUESTION TYPE 3: options stacked one per row -- one (badge=1 column, option=15 columns) pair spans
        /// all 16 fine columns. A shared question image shrinks the option cell to 11 columns and becomes a
        /// third column merged (real `w:vMerge`, not four separately-placed pictures) across all four option
        /// rows -- the image cell is built once, on the first row (`vMerge="restart"`); every following row
        /// repeats an empty `vMerge` continuation cell in that column, since a `w:tc` still has to exist in
        /// every row of a merged column -- Word does not infer it from the first row alone.
        /// </summary>
        private static async Task<List<Ooxml.TableRow>> BuildTextVerticalOptionRowsAsync(
            OptionModel[] options, string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var imageDrawing = await LoadSharedQuestionImageAsync(questionImageFile, mainPart, httpClient);
            var optionSpan = imageDrawing is null ? ContentFineColumnCount - 1 : ContentFineColumnCount - ImageFineColumnSpan - 1;
            var rows = new List<Ooxml.TableRow>();

            for (var i = 0; i < options.Length; i++)
            {
                var row = new Ooxml.TableRow();
                _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
                _ = row.AppendChild(BuildOptionBadgeCell(options[i].Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(options[i].Html, options[i].File, mainPart, httpClient, optionSpan));
                if (imageDrawing is not null)
                {
                    _ = row.AppendChild(i == 0 ? BuildImageMergeCellStart(imageDrawing, ImageFineColumnSpan) : BuildImageMergeCellContinue(ImageFineColumnSpan));
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// QUESTION TYPE 4: all four options are images, laid out side by side in one row -- same
        /// badge-cell-plus-content-cell pair every other layout uses (badge to the image's left, both
        /// vertically centered, badge tall enough to match the image), not a number stacked above a
        /// thumbnail: confirmed against the reference template's own image-options row. Same 4x(badge=1,
        /// content=3) column split as <see cref="BuildTextHorizontalOptionRowsAsync"/>'s image-less case --
        /// the content cells just hold images instead of text.
        /// </summary>
        private static async Task<List<Ooxml.TableRow>> BuildImageOptionsHorizontalRowsAsync(
            OptionModel[] options, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
            foreach (var option in options)
            {
                _ = row.AppendChild(BuildOptionBadgeCell(option.Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(null, option.File, mainPart, httpClient, 3, MaxImageOptionWidthPx));
            }

            return [row];
        }

        /// <summary>Rough HTML-stripped length used only to pick the options layout -- not real sanitization.</summary>
        private static int PlainTextLength(string? html) =>
            string.IsNullOrWhiteSpace(html) ? 0 : HtmlTagRegex().Replace(html, string.Empty).Trim().Length;

        [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
        private static partial System.Text.RegularExpressions.Regex HtmlTagRegex();

        /// <summary>An option's number badge -- always exactly 1 fine column wide, uniformly across every
        /// layout. The chip itself is <see cref="BuildNumberBadgeChip"/>, centered within this wider,
        /// unshaded outer cell.</summary>
        private static Ooxml.TableCell BuildOptionBadgeCell(string number)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = ContentFineColumnDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(BuildNumberBadgeChip(number, fontSizeHalfPoints: 18));

            // A table cell's content must end with a paragraph, not a table.
            _ = cell.AppendChild(new Ooxml.Paragraph());
            return cell;
        }

        /// <summary>A small, auto-sized, centered 1x1 table holding just the badge's grey chip (<see
        /// cref="BadgeGray"/>) around a number -- real box-model padding on all 4 sides via `w:tcMar`, unlike
        /// a run-level `w:shd`, which has no padding concept and paints tight to the glyph's own bounding
        /// box. Autofit (no <see cref="FixedTableLayout"/>) so the chip hugs its digit(s) rather than
        /// stretching to fill the wider outer grid cell it sits inside.</summary>
        private static Ooxml.Table BuildNumberBadgeChip(string number, int fontSizeHalfPoints)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = "0", Type = Ooxml.TableWidthUnitValues.Auto });
            _ = tableProperties.AppendChild(new Ooxml.TableJustification { Val = Ooxml.TableRowAlignmentValues.Center });
            _ = tableProperties.AppendChild(NoTableBorders());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, 500);

            var cellMargin = new Ooxml.TableCellMargin();
            _ = cellMargin.AppendChild(new Ooxml.TopMargin { Width = BadgeChipVerticalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellMargin.AppendChild(new Ooxml.LeftMargin { Width = BadgeChipHorizontalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellMargin.AppendChild(new Ooxml.BottomMargin { Width = BadgeChipVerticalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellMargin.AppendChild(new Ooxml.RightMargin { Width = BadgeChipHorizontalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });

            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = "0", Type = Ooxml.TableWidthUnitValues.Auto });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = BadgeGray });
            _ = cellProperties.AppendChild(cellMargin);
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);

            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(CreateRun(number, bold: true, colorHex: TextDark, fontSizeHalfPoints: fontSizeHalfPoints));
            _ = cell.AppendChild(paragraph);

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(cell);
            _ = table.AppendChild(row);
            return table;
        }

        private static async Task<Ooxml.TableCell> BuildOptionContentCellAsync(
            string? optionHtml, string? optionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient, int gridSpanCols, int? imageMaxWidthPx = null)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (gridSpanCols * ContentFineColumnDxa).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            if (gridSpanCols > 1)
            {
                _ = cellProperties.AppendChild(new Ooxml.GridSpan { Val = gridSpanCols });
            }

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
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, optionFile, httpClient, imageMaxWidthPx, null);
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

        /// <summary>First row of a vertically-merged shared-image column (2x2 or vertical layouts, see <see cref="LoadSharedQuestionImageAsync"/>) -- carries the actual picture and starts the merge.</summary>
        private static Ooxml.TableCell BuildImageMergeCellStart(Ooxml.Drawing? imageDrawing, int gridSpanCols)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (gridSpanCols * ContentFineColumnDxa).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            if (gridSpanCols > 1)
            {
                _ = cellProperties.AppendChild(new Ooxml.GridSpan { Val = gridSpanCols });
            }

            // CT_TcPr child order (ECMA-376 17.4.70): tcW, gridSpan, hMerge, vMerge, tcBorders, shd, ...,
            // vAlign, ... -- vMerge must precede tcBorders/vAlign, or Word's schema validator rejects the
            // element (confirmed via OpenXmlValidator: "unexpected child element vMerge" when tcBorders
            // came first).
            _ = cellProperties.AppendChild(new Ooxml.VerticalMerge { Val = Ooxml.MergedCellValues.Restart });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);

            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            if (imageDrawing is not null)
            {
                var run = new Ooxml.Run();
                _ = run.AppendChild(imageDrawing);
                _ = paragraph.AppendChild(run);
            }

            _ = cell.AppendChild(paragraph);
            return cell;
        }

        /// <summary>Continuation row of a vertically-merged image column -- an empty placeholder cell; Word requires one `w:tc` per row even inside a merge, it does not infer it from the first row.</summary>
        private static Ooxml.TableCell BuildImageMergeCellContinue(int gridSpanCols)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = (gridSpanCols * ContentFineColumnDxa).ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            if (gridSpanCols > 1)
            {
                _ = cellProperties.AppendChild(new Ooxml.GridSpan { Val = gridSpanCols });
            }

            _ = cellProperties.AppendChild(new Ooxml.VerticalMerge { Val = Ooxml.MergedCellValues.Continue });
            _ = cellProperties.AppendChild(NoTableCellBorders());
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(new Ooxml.Paragraph());
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
                var blockColumnWidthDxa = PageContentWidthDxa / AnswerKeyBlocksPerRow;
                AppendTableGrid(rowTable, [.. Enumerable.Repeat(blockColumnWidthDxa, AnswerKeyBlocksPerRow)]);

                var row = new Ooxml.TableRow();
                for (var b = 0; b < blocksInThisRow; b++)
                {
                    var questionStart = (blockStart + b) * AnswerKeyRowsPerBlock;
                    var questionEnd = Math.Min(questionStart + AnswerKeyRowsPerBlock, tests.Count);
                    var cell = new Ooxml.TableCell();
                    var cellProperties = new Ooxml.TableCellProperties();
                    _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = blockColumnWidthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
                    _ = cellProperties.AppendChild(NoTableCellBorders());
                    _ = cell.AppendChild(cellProperties);
                    _ = cell.AppendChild(new Ooxml.Paragraph());
                    _ = cell.AppendChild(BuildAnswerKeyBlock(tests, questionStart, questionEnd));

                    // A table cell's content must end with a paragraph, not a table -- without this,
                    // LibreOffice mis-renders every cell after the first in this row, stacking each block
                    // onto its own line instead of side by side (see BuildOptionBadgeCell/
                    // BuildQuestionNumberCell for the same rule applied to the nested badge-chip tables).
                    _ = cell.AppendChild(new Ooxml.Paragraph());
                    _ = row.AppendChild(cell);
                }

                for (var b = blocksInThisRow; b < AnswerKeyBlocksPerRow; b++)
                {
                    var emptyCell = new Ooxml.TableCell();
                    var emptyCellProperties = new Ooxml.TableCellProperties();
                    _ = emptyCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = blockColumnWidthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
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
