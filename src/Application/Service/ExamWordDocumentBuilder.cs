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
    /// docs/business/exams-and-content.md); every visual property here is set directly, without going
    /// through HTML/CSS at all. The Pdf export (<see cref="ExamPdfHtmlBuilder"/>) reproduces this same
    /// design in HTML and reuses this class's constants, layout rules and header shapes -- keep a change to
    /// how something is drawn here in step with it there.
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
    /// <param name="FooterLogo">The Gama "G" logo next to the footer's website link (exam-footer-logo.png, a 128px render of exam-footer-logo.svg).</param>
    /// <param name="FooterLogoSvg">The same logo as vector SVG (exam-footer-logo.svg), used by the Pdf export.</param>
    /// <param name="GamaWordmarkSvg">The same brand panel as vector art (exam-gama-wordmark.svg), for the Pdf export,
    /// where it stays sharp at any zoom; exam-gama-wordmark.png is rendered from it at 4x (2160x416) for Word, whose
    /// SVG support is uneven across viewers.</param>
    internal sealed record HeaderBrandAssets(byte[] GamaWordmark, byte[] GamaWordmarkSvg, byte[] ProfilePlaceholder, byte[] FooterWave, byte[] FooterLogo, byte[] FooterLogoSvg);

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
        internal const int PageWidthDxa = 11906;
        internal const int PageHeightDxa = 16838;
        internal const int PageMarginTopDxa = 2977;
        internal const int PageMarginRightDxa = 720;
        internal const int PageMarginBottomDxa = 720;
        internal const int PageMarginLeftDxa = 720;
        internal const int PageMarginHeaderDxa = 720;
        internal const int PageMarginFooterDxa = 0;
        internal const int PageContentWidthDxa = PageWidthDxa - PageMarginLeftDxa - PageMarginRightDxa;

        // The header background's shared coordinate space (the reference's own image4.svg, 547x104 units,
        // scaled to PageContentWidthDxa) and the height of its top panel band (shapes 2/3, y:0-48). The
        // header table's row heights are derived from these so the table ends exactly where the background
        // does, instead of the background's rounded bottom poking out below the table's last border.
        internal const int HeaderBackgroundReferenceWidth = 547;
        internal const int HeaderBackgroundReferenceHeight = 104;
        internal const int HeaderBackgroundPanelReferenceHeight = 48;
        internal const int HeaderBackgroundHeightDxa = PageContentWidthDxa * HeaderBackgroundReferenceHeight / HeaderBackgroundReferenceWidth;
        internal const int HeaderBrandRowHeightDxa = PageContentWidthDxa * HeaderBackgroundPanelReferenceHeight / HeaderBackgroundReferenceWidth;
        internal const int HeaderMetadataRowHeightDxa = 320;
        // The table's 0.5pt horizontal borders add their own height on top of the row heights above.
        internal const int HeaderRowBordersAllowanceDxa = 15;

        // The optional Topics row under the metadata row (2026-09-26, only when the exam has topics): one metadata-row
        // line, plus TopicsExtraLineDxa per wrapped line, up to TopicsMaxLines -- a longer list is cut with an ellipsis,
        // since the row (like the background it sits on) has a fixed height. TopicsCharsPerLine is a conservative
        // estimate of a full-width 10pt line.
        internal const int TopicsCharsPerLine = 95;
        internal const int TopicsMaxLines = 3;
        internal const int TopicsExtraLineDxa = 240;

        /// <summary>The Topics row's text ("title, title, ..."), cut to <see cref="TopicsMaxLines"/> lines, or
        /// <see langword="null"/> when the exam has no topics (then there's no row at all).</summary>
        internal static string? TopicsText(ExamInformationResponseDto.ExamDto? exam)
        {
            if (exam?.Topics is not { Count: > 0 } topics)
            {
                return null;
            }

            var text = string.Join(", ", topics);
            const int maxLength = TopicsCharsPerLine * TopicsMaxLines;
            return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd(' ', ',') + "\u2026";
        }

        /// <summary>How far the Topics row stretches the header background, in the background's own 547-wide
        /// coordinate space (<see cref="HeaderBackgroundReferenceWidth"/>); 0 without topics. Everything below the
        /// metadata band's top (y &gt; 80) moves down by this much (<see cref="HeaderBackgroundShapesFor"/>), so the
        /// rounded bottom and the Header Outline end under the Topics row instead of the metadata row.</summary>
        internal static int TopicsBackgroundExtension(ExamInformationResponseDto.ExamDto? exam)
        {
            if (TopicsText(exam) is not { } text)
            {
                return 0;
            }

            var lines = Math.Clamp((text.Length + TopicsCharsPerLine - 1) / TopicsCharsPerLine, 1, TopicsMaxLines);
            var rowHeightDxa = HeaderMetadataRowHeightDxa + ((lines - 1) * TopicsExtraLineDxa);
            return (int)Math.Ceiling(rowHeightDxa * (double)HeaderBackgroundReferenceWidth / PageContentWidthDxa);
        }

        /// <summary>The header background's height with <paramref name="extension"/> extra reference units.</summary>
        internal static int HeaderBackgroundHeightFor(int extension) =>
            PageContentWidthDxa * (HeaderBackgroundReferenceHeight + extension) / HeaderBackgroundReferenceWidth;

        /// <summary>The Topics row's height: all the room the background's extension adds, less its own top border.</summary>
        internal static int TopicsRowHeightFor(int extension) =>
            HeaderBackgroundHeightFor(extension) - HeaderBackgroundHeightDxa - HeaderRowBordersAllowanceDxa;

        /// <summary>The page's top margin, grown by the Topics row so the body still starts below the header.</summary>
        internal static int PageMarginTopFor(int extension) =>
            PageMarginTopDxa + HeaderBackgroundHeightFor(extension) - HeaderBackgroundHeightDxa;

        /// <summary><see cref="HeaderBackgroundShapes"/> with every point below the metadata band's top (y &gt; 80)
        /// moved down by <paramref name="extension"/> -- the bottom bands and the Header Outline stretch to cover the
        /// Topics row.</summary>
        internal static HeaderBackgroundShape[] HeaderBackgroundShapesFor(int extension) =>
            extension == 0
                ? HeaderBackgroundShapes
                : [.. HeaderBackgroundShapes.Select(shape => shape with
                {
                    Commands = [.. shape.Commands.Select(command => command with
                    {
                        Coordinates = [.. command.Coordinates.Select((value, i) => i % 2 == 1 && value > 80 ? value + extension : value)],
                    })],
                })];

        internal const long EmuPerPixel = 9525; // 96dpi CSS px -> EMU
        internal const int MaxImageWidthPx = 500;

        /// <summary>Cap for a question's own shared image attached to Text2x2/TextVertical as a side column
        /// (see LoadSharedQuestionImageAsync) -- that merged column is ~2300dxa (~1.6in) wide, well under
        /// MaxImageWidthPx. TextHorizontal doesn't use this: its own shared image gets a full-width row at its
        /// original size (capped at MaxImageWidthPx) instead of a side column (2026-09-23).</summary>
        internal const int MaxQuestionSideImageWidthPx = 150;

        /// <summary>Widest an image option can be in an all-image options row (see
        /// BuildImageOptionsHorizontalRowsAsync): its content cell's 3 fine columns minus Word's default 108dxa
        /// cell padding on each side, in 96dpi px (15dxa each) -- ~108px. An image option shows at its own
        /// original size and is only shrunk to this when wider (2026-09-24: it used to be forced to exactly 90px
        /// wide, enlarging small diagrams by up to ~20% and squeezing wide ones -- exam 1061 Q14's four options
        /// ended up at four different scales).</summary>
        internal const int MaxImageOptionWidthPx = ((3 * ContentFineColumnDxa) - (2 * 108)) / 15;
        internal const string BorderLightGray = "C5D0DB";
        internal const string RowGrayBg = "F4F7FB";
        internal const string TextDark = "172033";
        internal const string TextMuted = "5B6777";

        // Light-grey badge style (question number / option number) -- matches the reference template's own
        // real badge fill (#EDEDED), measured directly from its document.xml, 2026-09-22.
        internal const string BadgeGray = "EDEDED";

        // The question-number badge is a darker grey than the option-number one (2026-09-26, per request), so
        // question numbers stand out from their options.
        internal const string QuestionBadgeGray = "D9D9D9";

        // A badge's grey chip is a nested auto-width table (see BuildNumberBadgeChip), not the outer grid
        // cell itself, so it can carry real `w:tcMar` padding on all 4 sides -- a run-level `w:shd` (the
        // prior approach) has no padding concept and paints tight to the glyph's own bounding box.
        internal const int BadgeChipHorizontalPaddingDxa = 80;
        internal const int BadgeChipVerticalPaddingDxa = 40;

        /// <summary>
        /// The dark-blue rule drawn under every question (see <see cref="AppendSeparatorRows"/>) -- matches
        /// the reference template's own real separator color (#002060, `single` border, size 8 = 1pt),
        /// measured directly from its document.xml, 2026-09-22 (previously an approximate brand navy).
        /// </summary>
        internal const string SeparatorNavy = "002060";

        /// <summary>Vertical padding around each question's own content, closing a gap measured 2026-09-23
        /// against Temp.docx: its separator-to-content padding is ~33px/4.2mm above and ~30-34px/3.7-4.3mm
        /// below at 200dpi, while this export's (previously undeliberate, relying only on incidental
        /// rendering leftovers from the near-zero-height spacer rows) was roughly half that. These sizes are
        /// paragraph-mark font sizes for <see cref="AppendSeparatorRows"/>'s two spacer rows, not literal
        /// dxa/pt padding values -- tuned empirically against real rendered output, not derived by formula.</summary>
        internal const int QuestionBottomPaddingFontSizeHalfPoints = 9;

        internal const int QuestionTopPaddingFontSizeHalfPoints = 13;

        /// <summary>
        /// An option's plain-text length (HTML stripped) at or below which <see cref="QuestionLayoutType.TextHorizontal"/>
        /// applies (four options fit side by side in one row) -- above this but at/below
        /// <see cref="LongOptionTextThreshold"/>, <see cref="QuestionLayoutType.Text2x2"/> is used instead.
        /// </summary>
        internal const int ShortOptionTextThreshold = 12;

        /// <summary>
        /// An option's plain-text length (HTML stripped) above which the options layout switches to a
        /// single stacked column (<see cref="QuestionLayoutType.TextVertical"/>) -- matches the reference
        /// template, where short options ("ampere") sit multiple-per-row but longer ones ("concerned about
        /// the level...") each get a full-width row instead of being squeezed into a narrower cell.
        /// </summary>
        internal const int LongOptionTextThreshold = 28;

        // Only needs document-local uniqueness (OOXML drawing IDs aren't referenced across files), so a
        // simple incrementing counter is enough -- Random would be gratuitous and trips CA5394.
        private static long imageIdCounter;

        public static async Task<byte[]> BuildAsync(
            [NotNull] ExamInformationResponseDto data, HeaderBrandAssets brandAssets, string? watermarkText, Lazy<HttpClient> httpClient,
            bool googleDocsCompatible)
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
                    // is a group of rows in this same table (header row, then its own option rows, then two
                    // spacer rows -- one carrying the navy separator border -- before the next question's rows
                    // begin), sharing one tblGrid throughout. A one-table-per-question revert was tried
                    // 2026-09-23 as a test, hoping a whole table failing to fit would be a more reliable
                    // "move to next page" signal to LibreOffice than a `keepNext`-chained row boundary deep
                    // inside one shared table (see AppendQuestionAsync's own doc comment for the actual
                    // pagination issue this was chasing -- exam 1061's Q7, whose header row can still land on
                    // a different page than its own unusually tall options row). That test produced an
                    // identical result to the shared table (confirmed live, pixel-for-pixel), so it bought
                    // nothing and was reverted back to this, which does at least match the reference.
                    var questionsTable = BuildSharedQuestionTable();
                    for (var i = 0; i < data.Tests.Count; i++)
                    {
                        await AppendQuestionAsync(questionsTable, data.Tests[i], i, data.Tests.Count, mainPart, httpClient);
                    }

                    _ = body.AppendChild(questionsTable);
                    await AppendAnswerKeySectionAsync(body, data.Tests, mainPart, httpClient);
                }

                RemoveDefaultTableStyle(body);
                PreventRowsSplittingAcrossPages(body);

                _ = body.AppendChild(new Ooxml.SectionProperties());
                var sectionProperties = body.Elements<Ooxml.SectionProperties>().Single();
                _ = sectionProperties.AppendChild(new Ooxml.PageSize { Width = PageWidthDxa, Height = PageHeightDxa });
                _ = sectionProperties.AppendChild(new Ooxml.PageMargin
                {
                    Top = PageMarginTopFor(TopicsBackgroundExtension(data.Exam)),
                    Right = PageMarginRightDxa,
                    Bottom = PageMarginBottomDxa,
                    Left = PageMarginLeftDxa,
                    Header = PageMarginHeaderDxa,
                    Footer = PageMarginFooterDxa,
                    Gutter = 0,
                });

                await AddPageHeaderAndFooterAsync(mainPart, data.Exam, watermarkText, brandAssets, googleDocsCompatible);

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
        /// the title (the reference's Date cells dropped), then Name/Questions/Time/Difficulty Level (its School cell
        /// dropped), then Topics when the exam has any. The author-info cell shows the exam author's
        /// name from gama-api at export time (never a name baked into this public codebase); the portrait
        /// stays a generic placeholder.
        /// </summary>
        private static async Task<Ooxml.Table> BuildHeaderRowAsync<TPart>(
            ExamInformationResponseDto.ExamDto? exam, TPart headerPart, HeaderBrandAssets brandAssets, bool outerBordersFromBackground)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            const int contentWidthDxa = PageContentWidthDxa;

            // When the background's Header Outline shape draws the table's outer left/right/bottom edges (with
            // rounded bottom corners, which a table border can't do), the table's own outer borders are hidden.
            var outer = !outerBordersFromBackground;

            // A shared fine-grained 20-column grid lets each row group columns differently via gridSpan
            // (logo/portrait/text/QR in row 1; title in row 2; Name/.../Level in row 3; Topics in row 4) while
            // all rows still reference the same underlying tblGrid, same technique the reference's own
            // header1.xml uses (its real grid has even more columns, pct-based rather than dxa-based).
            const int columnCount = 20;
            const int columnUnitDxa = contentWidthDxa / columnCount; // 451
            const int lastColumnDxa = contentWidthDxa - (columnUnitDxa * (columnCount - 1)); // absorbs the rounding remainder

            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = contentWidthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = tableProperties.AppendChild(LightGrayTableBorders(outer));
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            var columnWidths = Enumerable.Repeat(columnUnitDxa, columnCount - 1).Append(lastColumnDxa).ToArray();
            AppendTableGrid(table, columnWidths);

            static int SpanWidth(int[] widths, int startColumn, int columnSpan) => widths.Skip(startColumn).Take(columnSpan).Sum();

            // Row 1: logo (9 cols, ~45%) | portrait (2 cols) | author info (5 cols) | QR (4 cols, ~20%).
            var brandRow = new Ooxml.TableRow();

            // Exactly the background's top panel band (shapes 2/3), so this row's bottom border lines up with
            // the panels' own bottom edge (2026-09-24). The 52px wordmark plus cell margins fits inside it.
            var brandRowProperties = new Ooxml.TableRowProperties();
            _ = brandRowProperties.AppendChild(new Ooxml.TableRowHeight { Val = HeaderBrandRowHeightDxa, HeightType = Ooxml.HeightRuleValues.Exact });
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

            var wordmarkCell = BorderedGridSpanCell(wordmarkParagraph, Ooxml.JustificationValues.Left, 9, SpanWidth(columnWidths, 0, 9).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Top, topBorder: false, leftBorder: false, rightBorder: false);
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

            _ = brandRow.AppendChild(BorderedGridSpanCell(profileParagraph, Ooxml.JustificationValues.Center, 2, SpanWidth(columnWidths, 9, 2).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, topBorder: false, leftBorder: false, rightBorder: false));

            // "By:" plus the exam author's name from gama-api's exams/{id} (2026-09-24), when it has one.
            var authorParagraph = new Ooxml.Paragraph();
            _ = authorParagraph.AppendChild(CreateRun("By: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            if (!string.IsNullOrEmpty(exam?.Author))
            {
                _ = authorParagraph.AppendChild(CreateRun(exam.Author, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            }
            _ = brandRow.AppendChild(BorderedGridSpanCell(authorParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 11, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, topBorder: false, leftBorder: false, rightBorder: false));

            var qrRun = new Ooxml.Run();
            if (!string.IsNullOrEmpty(exam?.QrCode))
            {
                var qrDrawing = await EmbedImageFromSourceAsync(headerPart, exam.QrCode, null, 50, 50);
                if (qrDrawing is not null)
                {
                    _ = qrRun.AppendChild(qrDrawing);
                }
            }

            var qrCell = BorderedGridSpanCell(WrapInParagraph(qrRun, Ooxml.JustificationValues.Right), Ooxml.JustificationValues.Right, 4, SpanWidth(columnWidths, 16, 4).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, topBorder: false, leftBorder: false, rightBorder: false);
            // A small ~10px right padding (2026-09-23, per request -- was 0/flush, see git history for the
            // original ~104px/13.2mm gap this closed) instead of 0, so the QR code sits just off the header's
            // own right edge rather than perfectly flush against it (matches the wordmark cell's own
            // no-left-padding treatment above, minus this one small gap). 10px at the same 96dpi CSS-px
            // convention EmuPerPixel/dxaToEmu use elsewhere in this file = 150dxa.
            const int qrRightPaddingDxa = 150;
            var qrCellProperties = qrCell.GetFirstChild<Ooxml.TableCellProperties>()!;
            var qrMargin = new Ooxml.TableCellMargin();
            _ = qrMargin.AppendChild(new Ooxml.RightMargin { Width = qrRightPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = qrCellProperties.GetFirstChild<Ooxml.TableCellVerticalAlignment>()!.InsertBeforeSelf(qrMargin);
            _ = brandRow.AppendChild(qrCell);
            _ = table.AppendChild(brandRow);

            // Row 2: the title across all 20 columns. The reference also had "Date:" label + value cells here --
            // dropped 2026-09-26 at the product owner's request, along with the School cell in row 3.
            // Takes whatever height the background has left after the brand and metadata rows, so the table
            // ends exactly at the background's bottom edge whether the title is one line or two (2026-09-24 --
            // it used to end ~1.5mm short, leaving the background's rounded bottom sticking out below it).
            // AtLeast rather than Exact so an unusually long 3-line title still shows in full.
            var titleRow = new Ooxml.TableRow();
            var titleRowProperties = new Ooxml.TableRowProperties();
            _ = titleRowProperties.AppendChild(new Ooxml.TableRowHeight
            {
                Val = HeaderBackgroundHeightDxa - HeaderBrandRowHeightDxa - HeaderMetadataRowHeightDxa - HeaderRowBordersAllowanceDxa,
                HeightType = Ooxml.HeightRuleValues.AtLeast,
            });
            _ = titleRow.AppendChild(titleRowProperties);
            var titleParagraph = new Ooxml.Paragraph();
            _ = titleParagraph.AppendChild(CreateRun(exam?.Title ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 24));
            _ = titleRow.AppendChild(BorderedGridSpanCell(titleParagraph, Ooxml.JustificationValues.Left, 20, SpanWidth(columnWidths, 0, 20).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, leftBorder: outer, rightBorder: outer));
            _ = table.AppendChild(titleRow);

            // Row 3: Name (5) | Questions (5) | Time (5) | Level (5). The reference also had a School cell (dropped
            // 2026-09-26) and a narrow empty spacer column after it, with Level squeezed into 3 columns -- dropped
            // 2026-09-25: "Level: Medium" wrapped onto a second line that the row's fixed height cut off (a red
            // overflow marker in LibreOffice).
            // With a Topics row below, the metadata row's bottom is an inner line, not the table's outer edge.
            var topicsText = TopicsText(exam);
            var metadataBottom = topicsText is not null || outer;
            var metadataRow = new Ooxml.TableRow();
            var metadataRowProperties = new Ooxml.TableRowProperties();
            _ = metadataRowProperties.AppendChild(new Ooxml.TableRowHeight { Val = HeaderMetadataRowHeightDxa, HeightType = Ooxml.HeightRuleValues.Exact });
            _ = metadataRow.AppendChild(metadataRowProperties);

            var nameParagraph = new Ooxml.Paragraph();
            _ = nameParagraph.AppendChild(CreateRun("Name:", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(nameParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 0, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, leftBorder: outer, bottomBorder: metadataBottom));

            var questionsParagraph = new Ooxml.Paragraph();
            _ = questionsParagraph.AppendChild(CreateRun("Questions: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = questionsParagraph.AppendChild(CreateRun(exam?.TestsCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(questionsParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 5, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, bottomBorder: metadataBottom));

            var timeParagraph = new Ooxml.Paragraph();
            _ = timeParagraph.AppendChild(CreateRun("Time: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = timeParagraph.AppendChild(CreateRun($"{exam?.ExamTime} min", bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(timeParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 10, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, bottomBorder: metadataBottom));

            var levelParagraph = new Ooxml.Paragraph();
            _ = levelParagraph.AppendChild(CreateRun("Difficulty Level: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = levelParagraph.AppendChild(CreateRun(exam?.Level ?? string.Empty, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
            _ = metadataRow.AppendChild(BorderedGridSpanCell(levelParagraph, Ooxml.JustificationValues.Left, 5, SpanWidth(columnWidths, 15, 5).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, rightBorder: outer, bottomBorder: metadataBottom));
            _ = table.AppendChild(metadataRow);

            // Row 4 (only when the exam has topics, 2026-09-26): "Topics:" and the titles across all 20 columns.
            if (topicsText is not null)
            {
                var topicsRow = new Ooxml.TableRow();
                var topicsRowProperties = new Ooxml.TableRowProperties();
                _ = topicsRowProperties.AppendChild(new Ooxml.TableRowHeight { Val = (uint)TopicsRowHeightFor(TopicsBackgroundExtension(exam)), HeightType = Ooxml.HeightRuleValues.Exact });
                _ = topicsRow.AppendChild(topicsRowProperties);

                var topicsParagraph = new Ooxml.Paragraph();
                _ = topicsParagraph.AppendChild(CreateRun("Topics: ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 20));
                _ = topicsParagraph.AppendChild(CreateRun(topicsText, bold: true, colorHex: TextDark, fontSizeHalfPoints: 20));
                _ = topicsRow.AppendChild(BorderedGridSpanCell(topicsParagraph, Ooxml.JustificationValues.Left, 20, SpanWidth(columnWidths, 0, 20).ToString(CultureInfo.InvariantCulture), Ooxml.TableVerticalAlignmentValues.Center, leftBorder: outer, rightBorder: outer, bottomBorder: outer));
                _ = table.AppendChild(topicsRow);
            }

            return table;
        }

        /// <summary>Thin light-gray grid lines on every side, matching the reference's traditional bordered-table look (no shading/fill).</summary>
        private static Ooxml.TableCell BorderedGridSpanCell(
            Ooxml.Paragraph paragraph, Ooxml.JustificationValues alignment, int gridSpan, string widthDxa, Ooxml.TableVerticalAlignmentValues verticalAlignment,
            bool topBorder = true, bool leftBorder = true, bool rightBorder = true, bool bottomBorder = true)
        {
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(new Ooxml.GridSpan { Val = gridSpan });
            _ = cellProperties.AppendChild(LightGrayCellBorders(top: topBorder, left: leftBorder, right: rightBorder, bottom: bottomBorder));
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

        /// <summary>Cell-level equivalent of <see cref="LightGrayTableBorders"/> -- thin light-gray on
        /// whichever sides are requested (all four by default), <see cref="Ooxml.BorderValues.None"/> on the
        /// rest. <see cref="BuildHeaderRowAsync{TPart}"/>'s brand row (logo/portrait/By:/QR) hides
        /// top/left/right on all 4 of its cells this way -- a 2026-09-23 test to see the brand row as one
        /// seamless bar (only each cell's bottom border left, separating it from the title row below) instead
        /// of a full grid box around every cell.</summary>
        private static Ooxml.TableCellBorders LightGrayCellBorders(bool top = true, bool left = true, bool right = true, bool bottom = true)
        {
            var borders = new Ooxml.TableCellBorders();
            _ = borders.AppendChild(top
                ? new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.TopBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(left
                ? new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(bottom
                ? new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.BottomBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(right
                ? new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            return borders;
        }

        /// <summary>Table-level equivalent of <see cref="LightGrayCellBorders"/> -- thin light-gray inside lines and
        /// top; the outer left/right/bottom sides too unless <paramref name="outerLeftRightBottom"/> is false
        /// (the background's Header Outline shape then draws them instead).</summary>
        private static Ooxml.TableBorders LightGrayTableBorders(bool outerLeftRightBottom)
        {
            var borders = new Ooxml.TableBorders();
            _ = borders.AppendChild(new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(outerLeftRightBottom
                ? new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(outerLeftRightBottom
                ? new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.BottomBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(outerLeftRightBottom
                ? new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 }
                : new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(new Ooxml.InsideHorizontalBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            _ = borders.AppendChild(new Ooxml.InsideVerticalBorder { Val = Ooxml.BorderValues.Single, Color = BorderLightGray, Size = 4 });
            return borders;
        }

        /// <summary>The header background's shapes, in drawing order, in the shared
        /// <see cref="HeaderBackgroundReferenceWidth"/> x <see cref="HeaderBackgroundReferenceHeight"/> coordinate space --
        /// one list shared by the Word header (<see cref="BuildHeaderBackgroundParagraph"/>, DrawingML) and the Pdf
        /// header (<see cref="ExamPdfHtmlBuilder"/>, inline SVG), so both always draw the same background. A
        /// null fill is an outline-only shape (0.5pt <see cref="BorderLightGray"/> stroke).</summary>
        internal static readonly HeaderBackgroundShape[] HeaderBackgroundShapes =
        [
            // Path data is the reference's own image4.svg, transcribed path-for-path (SVG's decimal
            // coordinates rounded to the nearest integer -- DrawingML path coordinates are plain integers).
            // Shape 1: subtle off-white band (#F9FAFB), y:56-104, all 4 corners rounded.
            new("F9FAFB", null,
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
            ]),

            // Shape 2: dark charcoal panel (#24292F), rounded top-left corner only (bottom-left squared off
            // 2026-09-23, per request), diagonal-cut right edge.
            new("24292F", null,
            [
                PathCommand.Move(0, 8),
                PathCommand.Cubic(0, 4, 4, 0, 8, 0),
                PathCommand.Line(219, 0),
                PathCommand.Cubic(225, 0, 230, 3, 233, 8),
                PathCommand.Line(249, 36),
                PathCommand.Cubic(252, 41, 248, 48, 242, 48),
                PathCommand.Line(0, 48),
                PathCommand.Close(),
            ]),

            // Shape 3 (Gray Diagonal Panel): light gray panel (#F2F4F7), complementary diagonal-cut left
            // edge (a byte-accurate transcription of the reference's own real image4.svg, including a real,
            // confirmed gap against the Black Panel there too -- see git history 2026-09-23 for the
            // left-extend attempt that was reverted per request). Right edge extended from x=491 to the
            // shape's own full x=547 width 2026-09-23, per request: the reference's own real x=491 right
            // edge leaves a real, confirmed gap of its own on that side too (~56 units/20mm, visible as
            // plain white behind/around the QR code in Temp.docx's own real render) -- unlike the left-side
            // gap, this one was asked to be closed rather than left matching the reference. Bottom-right
            // corner squared off (also 2026-09-23, per request) rather than kept rounded to x=539 like
            // shapes 1/4's own matching corners.
            new("F2F4F7", null,
            [
                PathCommand.Move(547, 48),
                PathCommand.Line(272, 48),
                PathCommand.Cubic(266, 48, 261, 45, 258, 40),
                PathCommand.Line(242, 12),
                PathCommand.Cubic(239, 7, 243, 0, 249, 0),
                PathCommand.Line(539, 0),
                PathCommand.Cubic(543, 0, 547, 4, 547, 8),
                PathCommand.Close(),
            ]),

            // Shape 4: light gray lower bar (#F2F4F7), y:80-104, flat top / rounded bottom corners.
            new("F2F4F7", null,
            [
                PathCommand.Move(0, 96),
                PathCommand.Cubic(0, 100, 4, 104, 8, 104),
                PathCommand.Line(539, 104),
                PathCommand.Cubic(543, 104, 547, 100, 547, 96),
                PathCommand.Line(547, 80),
                PathCommand.Line(0, 80),
                PathCommand.Close(),
            ]),

            // Shape 5 (a plain light-grey filler band continuing shape 4's band down to just above the
            // body's top margin) was removed 2026-09-23 per request -- see git history for the fillerHeightDxa
            // calculation and reasoning if it needs to come back.

            // Header Outline (2026-09-24): a Word table's corners can't be rounded, so the header table's outer
            // left/right/bottom borders are hidden (BuildHeaderRowAsync's outerBordersFromBackground) and drawn
            // here instead -- an unfilled line down both sides from the brand row's bottom edge (y=48) and around
            // the same rounded bottom corners as shapes 1/4, in the table's own border color/weight (0.5pt).
            // A white "mask" drawn in front of square table borders was tried first and dropped: LibreOffice
            // paints table borders over every header shape, even ones in front of text.
            new(null, BorderLightGray,
            [
                PathCommand.Move(0, HeaderBackgroundPanelReferenceHeight),
                PathCommand.Line(0, 96),
                PathCommand.Cubic(0, 100, 4, 104, 8, 104),
                PathCommand.Line(539, 104),
                PathCommand.Cubic(543, 104, 547, 100, 547, 96),
                PathCommand.Line(547, HeaderBackgroundPanelReferenceHeight),
            ]),
        ];

        internal readonly record struct HeaderBackgroundShape(string? FillHex, string? StrokeHex, PathCommand[] Commands);

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
        private static Ooxml.Paragraph BuildHeaderBackgroundParagraph(int topicsExtension)
        {
            const int contentWidthDxa = PageContentWidthDxa; // matches BuildHeaderRowAsync's own table width
            const int referenceWidth = HeaderBackgroundReferenceWidth;
            var referenceHeight = HeaderBackgroundReferenceHeight + topicsExtension;
            const long dxaToEmu = 635;

            var widthEmu = contentWidthDxa * dxaToEmu;
            var heightEmu = (long)Math.Round(widthEmu * ((double)referenceHeight / referenceWidth));
            var leftOffsetEmu = PageMarginLeftDxa * dxaToEmu;
            // This (otherwise empty) paragraph still occupies one line above the header table, even collapsed
            // (see below) -- pinned to exactly anchorLineDxa, and the shapes moved down by the same amount, so
            // the background's top edge starts exactly where the table does (2026-09-24: an unpinned ~1pt
            // line left the table ~25dxa below the shapes -- a hairline gap under the panels, and the table's
            // bottom border poking out below the background).
            const int anchorLineDxa = 20;
            var topOffsetEmu = (PageMarginHeaderDxa + anchorLineDxa) * dxaToEmu;

            // Floating (behindDoc) drawings don't contribute to the paragraph's own line-height metrics --
            // this paragraph is otherwise empty, so without shrinking its mark run's own font size it still
            // takes up a default (~11pt) line, pushing the table below it down and away from where the
            // shapes are actually anchored (page-relative, matching the table's own true top). A near-zero
            // mark font size collapses that gap.
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = anchorLineDxa.ToString(CultureInfo.InvariantCulture), LineRule = Ooxml.LineSpacingRuleValues.Exact });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);

            var run = new Ooxml.Run();
            var runProperties = new Ooxml.RunProperties();
            _ = runProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = run.AppendChild(runProperties);

            foreach (var shape in HeaderBackgroundShapesFor(topicsExtension))
            {
                _ = run.AppendChild(BuildBackgroundShapeDrawing(
                    shape.FillHex, widthEmu, heightEmu, leftOffsetEmu, topOffsetEmu, referenceWidth, referenceHeight, shape.Commands,
                    strokeHex: shape.StrokeHex, strokeWidthEmu: shape.StrokeHex is null ? 0 : 6350));
            }

            _ = paragraph.AppendChild(run);
            return paragraph;
        }

        /// <summary>One segment of an <c>a:custGeom</c> path -- a tiny DSL so each shape's real SVG path data (see BuildHeaderBackgroundParagraph) reads the same shape as the source it was transcribed from.</summary>
        internal readonly record struct PathCommand(char Kind, int[] Coordinates)
        {
            public static PathCommand Move(int x, int y) => new('M', [x, y]);
            public static PathCommand Line(int x, int y) => new('L', [x, y]);
            public static PathCommand Cubic(int x1, int y1, int x2, int y2, int x3, int y3) => new('C', [x1, y1, x2, y2, x3, y3]);
            public static PathCommand Close() => new('Z', []);
        }

        private static AlternateContent BuildBackgroundShapeDrawing(
            string? fillHex, long widthEmu, long heightEmu, long leftOffsetEmu, long topOffsetEmu,
            int pathWidth, int pathHeight, PathCommand[] commands, string? strokeHex = null, int strokeWidthEmu = 0)
        {
            // Shares imageIdCounter with the picture-embedding helpers (BuildImageGraphic) -- docPr/wp:anchor
            // ids must be unique across the whole document, not just among these 4 shapes, or Word's
            // validator (unlike LibreOffice) flags duplicate ids.
            var drawingId = (uint)Interlocked.Increment(ref imageIdCounter);
            var path = new A.Path
            {
                Width = pathWidth,
                Height = pathHeight,
                Fill = fillHex is null ? A.PathFillModeValues.None : A.PathFillModeValues.Norm,
                Stroke = strokeHex is not null,
            };
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
            if (fillHex is null)
            {
                _ = shapeProperties.AppendChild(new A.NoFill());
            }
            else
            {
                var solidFill = new A.SolidFill();
                _ = solidFill.AppendChild(new A.RgbColorModelHex { Val = fillHex });
                _ = shapeProperties.AppendChild(solidFill);
            }

            var outline = new A.Outline();
            if (strokeHex is null)
            {
                _ = outline.AppendChild(new A.NoFill());
            }
            else
            {
                outline.Width = strokeWidthEmu;
                var strokeFill = new A.SolidFill();
                _ = strokeFill.AppendChild(new A.RgbColorModelHex { Val = strokeHex });
                _ = outline.AppendChild(strokeFill);
            }

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
        internal readonly record struct OptionModel(string Number, string? Html, string? File);

        internal static OptionModel[] GetOptionModels(ExamInformationResponseDto.TestDto test) =>
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
        internal static QuestionLayoutType ClassifyLayout(ExamInformationResponseDto.TestDto test, OptionModel[] options)
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
        internal const int QuestionNumberColumnDxa = 658;

        /// <summary>Width (dxa) of each of the <see cref="ContentFineColumnCount"/> equal fine columns the
        /// content area (right of the question-number column) is divided into -- every layout expresses
        /// itself as a `w:gridSpan` combination of these, so <c>QuestionNumberColumnDxa + ContentFineColumnCount
        /// * ContentFineColumnDxa</c> must equal <see cref="PageContentWidthDxa"/> exactly.</summary>
        internal const int ContentFineColumnDxa = 613;

        /// <summary>Fine columns in the content area. Chosen as the smallest count that lets every layout's
        /// badge/option/image split land on whole-column boundaries: <see cref="QuestionLayoutType.TextHorizontal"/>/
        /// <see cref="QuestionLayoutType.ImageOptionsHorizontal"/> need 4 (badge+content) pairs (8 columns);
        /// with a shared image attached (see <see cref="LoadSharedQuestionImageAsync"/>), <see
        /// cref="QuestionLayoutType.Text2x2"/>/<see cref="QuestionLayoutType.TextVertical"/> give the image
        /// the last <see cref="ImageFineColumnSpan"/> columns and split the remaining 12 evenly for their own
        /// badge/option pairs (<see cref="QuestionLayoutType.TextHorizontal"/> instead puts a wide shared
        /// image in its own full-width row above the options -- see <see
        /// cref="BuildTextHorizontalOptionRowsAsync"/> -- so it never shrinks its own option columns for
        /// one).</summary>
        internal const int ContentFineColumnCount = 16;

        /// <summary>How many of the <see cref="ContentFineColumnCount"/> fine columns a shared question image
        /// occupies in <see cref="QuestionLayoutType.Text2x2"/>/<see cref="QuestionLayoutType.TextVertical"/>
        /// (see <see cref="LoadSharedQuestionImageAsync"/>), always the last ones in the row. Not used by
        /// <see cref="QuestionLayoutType.TextHorizontal"/>, whose own shared image gets a full-width row
        /// instead of a side column.</summary>
        internal const int ImageFineColumnSpan = 4;

        /// <summary>
        /// One shared table for the whole exam, matching the reference template's own real structure exactly
        /// (confirmed 2026-09-22 by reading its document.xml cell-by-cell: one continuous table for every
        /// question, not a separate table per question -- a one-table-per-question revert was tried
        /// 2026-09-23 chasing a pagination issue, see <see cref="BuildAsync"/>'s own doc comment, and reverted
        /// back after confirming it made no difference) -- <see cref="AppendQuestionAsync"/> appends each
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
        /// Appends one question to the shared table as a single wrapper row, then its two thin spacer rows --
        /// the first carrying the navy separator border, the second blank padding beneath it -- matching the
        /// reference's own between-question spacing. The very last question only gets the bordered spacer
        /// row, no trailing blank one (matches the reference, which ends its table right after the last
        /// question's separator).
        /// <para>
        /// The wrapper row's one cell spans the whole grid and holds a nested table with exactly the shared
        /// table's own grid (<see cref="BuildSharedQuestionTable"/>), carrying the question's real rows: the
        /// number/text row (<see cref="AppendQuestionHeaderRowsAsync"/>), then its option rows (or, for a
        /// descriptive question with its own image, one centered-image row). It renders identically to those
        /// rows sitting directly in the shared table, but it's what actually keeps a question on one page
        /// (2026-09-24): with the rows directly in the shared table, <c>w:cantSplit</c> only stopped each
        /// row splitting internally, and the <c>w:keepNext</c> chaining meant to glue them together is
        /// honored by Word but ignored inside tables by LibreOffice (and Google Docs) -- found live on exam
        /// 1061 Q7 and exam 1000 Q4, whose number/text row stayed at the bottom of one page while its
        /// image/options moved to the next. One wrapper row with <c>w:cantSplit</c>
        /// (<see cref="PreventRowsSplittingAcrossPages"/>) moves as a whole in every renderer (a question
        /// taller than a whole page still splits, there being no other option).
        /// </para>
        /// </summary>
        private static async Task AppendQuestionAsync(
            Ooxml.Table table, ExamInformationResponseDto.TestDto test, int index, int totalCount, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var questionTable = BuildSharedQuestionTable();
            await AppendQuestionHeaderRowsAsync(questionTable, test, index + 1, mainPart, httpClient);

            if (test.HasOptions)
            {
                foreach (var row in await BuildOptionRowsAsync(test, mainPart, httpClient))
                {
                    _ = questionTable.AppendChild(row);
                }
            }
            else if (!string.IsNullOrEmpty(test.QuestionFile))
            {
                // Descriptive question (no options at all, see HasOptions) with its own image: no options
                // layout to share it with, so it simply sits centered in its own row.
                _ = questionTable.AppendChild(await BuildDescriptiveImageRowAsync(test.QuestionFile, mainPart, httpClient));
            }

            AppendWrappedRowGroup(table, questionTable, includeTrailingBlankRow: index < totalCount - 1);
        }

        /// <summary>
        /// Appends <paramref name="innerTable"/> (built by <see cref="BuildSharedQuestionTable"/>, same grid) to
        /// <paramref name="table"/> as ONE wrapper row -- so it can never split across a page break, see
        /// <see cref="AppendQuestionAsync"/> -- followed by its separator rows. Shared by questions and by the
        /// answer section's descriptive answers.
        /// </summary>
        private static void AppendWrappedRowGroup(Ooxml.Table table, Ooxml.Table innerTable, bool includeTrailingBlankRow)
        {
            var wrapperCell = new Ooxml.TableCell();
            var wrapperCellProperties = new Ooxml.TableCellProperties();
            _ = wrapperCellProperties.AppendChild(new Ooxml.TableCellWidth
            {
                Width = (QuestionNumberColumnDxa + (ContentFineColumnDxa * ContentFineColumnCount)).ToString(CultureInfo.InvariantCulture),
                Type = Ooxml.TableWidthUnitValues.Dxa,
            });
            _ = wrapperCellProperties.AppendChild(new Ooxml.GridSpan { Val = 1 + ContentFineColumnCount });
            var wrapperMargin = new Ooxml.TableCellMargin();
            _ = wrapperMargin.AppendChild(new Ooxml.TopMargin { Width = "0", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = wrapperMargin.AppendChild(new Ooxml.LeftMargin { Width = "0", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = wrapperMargin.AppendChild(new Ooxml.BottomMargin { Width = "0", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = wrapperMargin.AppendChild(new Ooxml.RightMargin { Width = "0", Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = wrapperCellProperties.AppendChild(wrapperMargin);
            _ = wrapperCell.AppendChild(wrapperCellProperties);
            _ = wrapperCell.AppendChild(innerTable);

            // A cell's content must end with a paragraph, not a table; collapsed to ~nothing so it adds no
            // visible gap under the question.
            _ = wrapperCell.AppendChild(BuildCollapsedParagraph());

            var wrapperRow = new Ooxml.TableRow();
            _ = wrapperRow.AppendChild(wrapperCell);
            _ = table.AppendChild(wrapperRow);

            // Word also honors keepNext, so there it additionally keeps the question with its own separator
            // row below it rather than leaving that line alone at the top of the next page.
            SetKeepNextOnAllParagraphs(wrapperRow);

            AppendSeparatorRows(table, includeTrailingBlankRow);
        }

        private static Ooxml.Paragraph BuildCollapsedParagraph()
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = Ooxml.LineSpacingRuleValues.Exact });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);
            return paragraph;
        }

        private static void SetKeepNextOnAllParagraphs(Ooxml.TableRow row)
        {
            foreach (var paragraph in row.Descendants<Ooxml.Paragraph>())
            {
                var properties = paragraph.Elements<Ooxml.ParagraphProperties>().FirstOrDefault();
                if (properties is null)
                {
                    properties = new Ooxml.ParagraphProperties();
                    _ = paragraph.PrependChild(properties);
                }

                if (!properties.Elements<Ooxml.KeepNext>().Any())
                {
                    _ = properties.PrependChild(new Ooxml.KeepNext());
                }
            }
        }

        /// <summary>
        /// The single row every question starts with: a question-number badge beside the question-text cell,
        /// which simply grows to whatever height a long or multi-paragraph question needs -- an ordinary
        /// OOXML table cell has no fixed height unless one is explicitly set (none is, here), so nothing
        /// clips. This used to be two rows with the text cell `w:vMerge`-spanned across them (see git history
        /// 2026-09-23 for why): that let a wrapping question grow into a second row instead of being clipped,
        /// but a `w:vMerge` continuation turned out to be a real, reproducible page-break opportunity in
        /// LibreOffice that neither `w:cantSplit` (<see cref="PreventRowsSplittingAcrossPages"/>) nor
        /// `w:keepNext` chaining (<see cref="AppendQuestionAsync"/>) prevented -- found live on exam 1061's
        /// Q7, whose two-sentence question text split apart at exactly that `vMerge` boundary, moving only
        /// the second sentence (plus the options/image that follow) onto the next page while the first
        /// sentence and the question number stayed behind alone. A single, ordinary (non-merged) cell doesn't
        /// have that failure mode: `cantSplit` on this one row is then sufficient to keep the whole question
        /// number + text together, and `keepNext` chaining handles gluing this row to the option rows after
        /// it.
        /// </summary>
        private static Task AppendQuestionHeaderRowsAsync(
            Ooxml.Table table, ExamInformationResponseDto.TestDto test, int number, MainDocumentPart mainPart, Lazy<HttpClient> httpClient) =>
            AppendNumberedTextRowAsync(table, test.Question, number, new ExamWordRichText.RunFormat(true, false, false, false, false, TextDark, 22), mainPart, httpClient);

        /// <summary>One row: the number badge beside a full-width cell holding <paramref name="html"/> as rich text
        /// in <paramref name="format"/> -- a question's own text (bold 11pt), or a descriptive answer (regular 10pt).</summary>
        private static async Task AppendNumberedTextRowAsync(
            Ooxml.Table table, string? html, int number, ExamWordRichText.RunFormat format, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var questionParagraphs = await ExamWordRichText.ParseToParagraphsAsync(html, mainPart, httpClient, format);

            var contentWidthDxa = (ContentFineColumnDxa * ContentFineColumnCount).ToString(CultureInfo.InvariantCulture);

            var textCell = new Ooxml.TableCell();
            var textCellProperties = new Ooxml.TableCellProperties();
            _ = textCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = contentWidthDxa, Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = textCellProperties.AppendChild(new Ooxml.GridSpan { Val = ContentFineColumnCount });
            _ = textCellProperties.AppendChild(NoTableCellBorders());
            _ = textCell.AppendChild(textCellProperties);
            foreach (var paragraph in questionParagraphs)
            {
                _ = textCell.AppendChild(paragraph);
            }

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildQuestionNumberCell(number, showNumber: true));
            _ = row.AppendChild(textCell);
            _ = table.AppendChild(row);
        }

        /// <summary>The question-number column's own cell -- a light-grey badge chip (see
        /// <see cref="BuildNumberBadgeChip"/>) throughout the question's row group, but the digit itself only
        /// drawn on the block's first row (<paramref name="showNumber"/> false everywhere else: every option
        /// row, matching the reference, which never vertically merges this column, just repeats a blank cell
        /// beneath it -- no chip is drawn there since there's no digit).</summary>
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
                _ = cell.AppendChild(BuildNumberBadgeChip(number.ToString(CultureInfo.InvariantCulture), fontSizeHalfPoints: 22, QuestionBadgeGray));
            }

            // A table cell's content must end with a paragraph, not a table (see the schema-order note on
            // BuildQuestionTableAsync's history elsewhere in this file) -- always appended, whether or not
            // the chip table above was added, so a cell with no number still has valid, empty content.
            _ = cell.AppendChild(new Ooxml.Paragraph());
            return cell;
        }

        /// <summary>
        /// The two rows between one question's rows and the next: the first carries the navy separator as
        /// its own bottom border (a real table border, not a drawn shape) and gives real bottom-padding
        /// below the question's own content before that line (<see cref="QuestionBottomPaddingFontSizeHalfPoints"/>),
        /// the second gives real top-padding above the next question's content
        /// (<see cref="QuestionTopPaddingFontSizeHalfPoints"/>) -- both via <see cref="BuildSpacerParagraph"/>,
        /// not collapsed to near-zero like most of this file's other spacer rows (a near-zero font size
        /// there instead). The separator border itself matches the reference's own
        /// real separator exactly (measured directly from its document.xml, 2026-09-22): border `single`,
        /// size 8 (1pt), color <see cref="SeparatorNavy"/>. The last question in the exam only gets the
        /// bordered row.
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
            _ = borderedCell.AppendChild(BuildSpacerParagraph(QuestionBottomPaddingFontSizeHalfPoints));

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
            _ = blankCell.AppendChild(BuildSpacerParagraph(QuestionTopPaddingFontSizeHalfPoints));

            var blankRow = new Ooxml.TableRow();
            _ = blankRow.AppendChild(blankCell);
            _ = table.AppendChild(blankRow);
        }

        /// <summary>Empty paragraph whose height is entirely driven by <paramref name="fontSizeHalfPoints"/>
        /// (via the paragraph mark's own run size, auto line spacing) -- a near-zero size (e.g. 2) collapses
        /// a row close to zero height instead of Word's default (~11pt) line height, the same technique
        /// <see cref="BuildHeaderBackgroundParagraph"/> uses; <see cref="AppendSeparatorRows"/> also uses
        /// larger sizes here for real, deliberate padding rows.</summary>
        private static Ooxml.Paragraph BuildSpacerParagraph(int fontSizeHalfPoints)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = Ooxml.LineSpacingRuleValues.Auto });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = fontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);
            return paragraph;
        }

        /// <summary>A descriptive question's (no options, see HasOptions) own image, centered in a single
        /// full-width row -- no options layout exists to share it with.</summary>
        private static async Task<Ooxml.TableRow> BuildDescriptiveImageRowAsync(string questionFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var imageDrawing = await EmbedImageFromSourceAsync(mainPart, questionFile, httpClient, null, null);
            return BuildCenteredFullWidthImageRow(imageDrawing);
        }

        /// <summary>One full-width, centered-image row spanning all <see cref="ContentFineColumnCount"/> fine
        /// columns beside a blank badge cell -- shared by <see cref="BuildDescriptiveImageRowAsync"/> (a
        /// descriptive question's only content) and <see cref="BuildTextHorizontalOptionRowsAsync"/> (a wide
        /// shared image placed above its options instead of squeezed into a narrow side column, 2026-09-23,
        /// per request, for better UX).</summary>
        private static Ooxml.TableRow BuildCenteredFullWidthImageRow(Ooxml.Drawing? imageDrawing)
        {
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
        /// file), for <see cref="QuestionLayoutType.Text2x2"/>/<see cref="QuestionLayoutType.TextVertical"/>'s
        /// own merged side column. Returns <see langword="null"/> when there is no image, which every caller
        /// treats as "render the plain, image-less variant of this layout".
        /// </summary>
        private static Task<Ooxml.Drawing?> LoadSharedQuestionImageAsync(string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient) =>
            string.IsNullOrEmpty(questionImageFile)
                ? Task.FromResult<Ooxml.Drawing?>(null)
                : EmbedImageFromSourceAsync(mainPart, questionImageFile, httpClient, MaxQuestionSideImageWidthPx, null);

        /// <summary>
        /// QUESTION TYPE 1: all four options in one row, each a narrow badge cell plus its own answer cell --
        /// 4 (badge=1 column, option=3 columns) pairs span all 16 fine columns, always at full width. Unlike
        /// the 2x2/vertical variants below, a shared question image here never shrinks the option columns to
        /// share the row with them -- it gets its own full-width row above the options instead (<see
        /// cref="BuildCenteredFullWidthImageRow"/>), fixed 2026-09-23 per request for better UX: a wide shared
        /// image (e.g. exam 1061 Q10's own multi-column comparison table) read as cramped squeezed into a
        /// narrow side column next to 4 already-tight option cells. That row shows the image at its own
        /// original size, only shrunk when wider than <see cref="MaxImageWidthPx"/> -- never enlarged (a
        /// fixed 500px width blew small sources like Q13's 324px diagram up to half a page, and blurrier).
        /// </summary>
        private static async Task<List<Ooxml.TableRow>> BuildTextHorizontalOptionRowsAsync(
            OptionModel[] options, string? questionImageFile, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            var rows = new List<Ooxml.TableRow>();
            if (!string.IsNullOrEmpty(questionImageFile))
            {
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, questionImageFile, httpClient, null, null);
                rows.Add(BuildCenteredFullWidthImageRow(imageDrawing));
            }

            var row = new Ooxml.TableRow();
            _ = row.AppendChild(BuildQuestionNumberCell(0, showNumber: false));
            foreach (var option in options)
            {
                _ = row.AppendChild(BuildOptionBadgeCell(option.Number));
                _ = row.AppendChild(await BuildOptionContentCellAsync(option.Html, option.File, mainPart, httpClient, 3));
            }

            rows.Add(row);
            return rows;
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
            _ = cell.AppendChild(BuildNumberBadgeChip(number, fontSizeHalfPoints: 18, BadgeGray));

            // A table cell's content must end with a paragraph, not a table.
            _ = cell.AppendChild(new Ooxml.Paragraph());
            return cell;
        }

        /// <summary>A small, auto-sized, centered 1x1 table holding just the badge's grey chip
        /// (<paramref name="fillHex"/>: <see cref="QuestionBadgeGray"/> or <see cref="BadgeGray"/>) around a number -- real box-model padding on all 4 sides via `w:tcMar`, unlike
        /// a run-level `w:shd`, which has no padding concept and paints tight to the glyph's own bounding
        /// box. Autofit (no <see cref="FixedTableLayout"/>) so the chip hugs its digit(s) rather than
        /// stretching to fill the wider outer grid cell it sits inside.</summary>
        private static Ooxml.Table BuildNumberBadgeChip(string number, int fontSizeHalfPoints, string fillHex)
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
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = fillHex });
            _ = cellProperties.AppendChild(cellMargin);
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);

            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            // Explicit zero spacing (2026-09-26): the document has no styles part, so Word 2019 falls back to its own
            // default of 8pt after every paragraph, which showed up inside the grey chip as a much bigger bottom
            // padding than top/left/right (LibreOffice's fallback is 0, so it never showed there).
            _ = ZeroSpacingParagraphProperties(paragraph);
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
                var imageDrawing = await EmbedImageFromSourceAsync(mainPart, optionFile, httpClient, null, null, imageMaxWidthPx);
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

        // All AnswerKey* geometry/color/font constants below were measured directly from Temp.docx's own
        // document.xml/styles.xml, 2026-09-23 (see docs/business/exams-and-content.md for the walkthrough).
        internal const string AnswerKeyHeaderYellow = "FFE599"; // was FBE1A0 -- corrected to the reference's real fill
        internal const string AnswerKeyBorderColor = "000000"; // the reference's TableGrid style border ("auto"/sz4 renders as black, confirmed by sampling rendered pixels)
        internal const int AnswerKeyRowsPerBlock = 10;
        internal const int AnswerKeyBlocksPerRow = 4;
        internal const int AnswerKeyNumberColumnDxa = 550; // reference's Q# column varies 545-571dxa across blocks; 550 is representative
        internal const int AnswerKeyOptionColumnDxa = 447; // reference's 4 option columns vary 445-451dxa; 447 is representative
        internal const int AnswerKeyBlockContentWidthDxa = AnswerKeyNumberColumnDxa + (AnswerKeyOptionColumnDxa * 4);
        /// <summary>The blank gap after every block in a row, including the last -- solved so 4 blocks + 4
        /// gaps fill <see cref="PageContentWidthDxa"/> exactly. The reference itself only gaps the first 3
        /// (its own last block sits flush against the page's right margin, landing there just because that's
        /// where the leftover column-width math put it, not as a deliberate design choice) -- gapping the
        /// last block too, at a correspondingly smaller 278dxa rather than the ungapped-3 case's 371dxa,
        /// keeps the row from looking flush/stuck against the margin on the right.</summary>
        internal const int AnswerKeyGapColumnDxa = (PageContentWidthDxa - (AnswerKeyBlockContentWidthDxa * AnswerKeyBlocksPerRow)) / AnswerKeyBlocksPerRow;
        internal const int AnswerKeyCellFontSizeHalfPoints = 24; // was 18 (9pt); the reference's header/question-number/option-mark runs are all sz=24 (12pt)
        internal const int AnswerKeyCellHorizontalPaddingDxa = 108; // matches the reference's own tblCellMar left/right

        /// <summary>
        /// Appends the "Answer Key" page: a grid of mini answer-sheet tables (question number + a
        /// filled/empty square per option, filled from <see cref="ExamInformationResponseDto.TestDto.CorrectOption"/>),
        /// 10 questions per block, up to 4 blocks per row -- the same layout as the reference template -- then the
        /// descriptive questions' worked answers (<see cref="AppendDescriptiveAnswersAsync"/>).
        /// Deliberately one page's worth of blocks laid out top-to-bottom/left-to-right with no pagination
        /// of its own; for very large question counts this may run onto a second page via Word's own
        /// natural overflow (the blocks aren't marked non-splittable), which is acceptable for a template
        /// iteration.
        /// </summary>
        private static async Task AppendAnswerKeySectionAsync(
            Ooxml.Body body, List<ExamInformationResponseDto.TestDto> tests, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            // The multiple-choice grid only when the exam has multiple-choice questions (an all-descriptive exam
            // like 831 would otherwise get a page of empty squares), then any descriptive answers; no page at all
            // when there's neither.
            var hasChoiceQuestions = tests.Any(t => t.HasOptions);
            var descriptiveAnswers = DescriptiveAnswers(tests);
            if (!hasChoiceQuestions && descriptiveAnswers.Count == 0)
            {
                return;
            }

            var pageBreakRun = new Ooxml.Run();
            _ = pageBreakRun.AppendChild(new Ooxml.Break { Type = Ooxml.BreakValues.Page });
            var pageBreakParagraph = new Ooxml.Paragraph();
            _ = pageBreakParagraph.AppendChild(pageBreakRun);
            _ = body.AppendChild(pageBreakParagraph);

            // The "above" and "below" gaps are controlled by two separate paragraphs, not one -- measured
            // (2026-09-23, 200dpi renders) that a single paragraph's own `w:spacing before` stops rendering
            // reliably once that same paragraph also sets a tight `line`/`lineRule=exact` (used below to
            // shrink the "below" gap): LibreOffice appeared to ignore `before` entirely once `line` dropped
            // to a near-zero exact value, even though both attributes sit on the same, correctly-generated
            // `w:pPr`. Splitting them avoids that interaction: a dedicated spacer paragraph (default line
            // metrics, where `before`/`after` are known to render correctly) creates the gap above the
            // title, and the heading paragraph's own tight line height only has to control the gap below it.
            var topSpacer = new Ooxml.Paragraph();
            var topSpacerProperties = new Ooxml.ParagraphProperties();
            _ = topSpacerProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "310" });
            var topSpacerMarkRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = topSpacerMarkRunProperties.AppendChild(new Ooxml.FontSize { Val = "2" });
            _ = topSpacerProperties.AppendChild(topSpacerMarkRunProperties);
            _ = topSpacer.AppendChild(topSpacerProperties);
            _ = body.AppendChild(topSpacer);

            var heading = new Ooxml.Paragraph();
            var headingProperties = new Ooxml.ParagraphProperties();
            _ = headingProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "0", Line = "1", LineRule = Ooxml.LineSpacingRuleValues.Exact });
            _ = heading.AppendChild(headingProperties);
            _ = heading.AppendChild(CreateRun("Answer Key", bold: true, colorHex: TextDark, fontSizeHalfPoints: 32));
            _ = body.AppendChild(heading);

            if (hasChoiceQuestions)
            {
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

                    // Column j's outer slot is the block's own content width plus a trailing gap -- every
                    // column, including the last, so the row's rightmost block gets the same breathing room on
                    // its right as every other block does, rather than sitting flush against the page's right
                    // margin (see AnswerKeyGapColumnDxa). Applies uniformly regardless of whether a slot ends up
                    // holding a real block or an empty filler cell.
                    var outerColumnWidthsDxa = Enumerable.Range(0, AnswerKeyBlocksPerRow)
                        .Select(_ => AnswerKeyBlockContentWidthDxa + AnswerKeyGapColumnDxa)
                        .ToArray();
                    AppendTableGrid(rowTable, outerColumnWidthsDxa);

                    var row = new Ooxml.TableRow();
                    for (var b = 0; b < blocksInThisRow; b++)
                    {
                        var questionStart = (blockStart + b) * AnswerKeyRowsPerBlock;
                        var questionEnd = Math.Min(questionStart + AnswerKeyRowsPerBlock, tests.Count);
                        var cell = new Ooxml.TableCell();
                        var cellProperties = new Ooxml.TableCellProperties();
                        _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = outerColumnWidthsDxa[b].ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
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
                        _ = emptyCellProperties.AppendChild(new Ooxml.TableCellWidth { Width = outerColumnWidthsDxa[b].ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
                        _ = emptyCellProperties.AppendChild(NoTableCellBorders());
                        _ = emptyCell.AppendChild(emptyCellProperties);
                        _ = emptyCell.AppendChild(new Ooxml.Paragraph());
                        _ = row.AppendChild(emptyCell);
                    }

                    _ = rowTable.AppendChild(row);
                    _ = body.AppendChild(rowTable);
                    _ = body.AppendChild(BuildAnswerKeyRowGapParagraph());
                }
            }

            if (descriptiveAnswers.Count > 0)
            {
                await AppendDescriptiveAnswersAsync(body, descriptiveAnswers, withHeading: hasChoiceQuestions, mainPart, httpClient);
            }
        }

        /// <summary>Descriptive questions (no options, see <see cref="ExamInformationResponseDto.TestDto.HasOptions"/>)
        /// that have a worked answer or answer image, with their 1-based question number.</summary>
        internal static List<(int Number, ExamInformationResponseDto.TestDto Test)> DescriptiveAnswers(List<ExamInformationResponseDto.TestDto> tests) =>
        [
            .. tests.Select((test, index) => (Number: index + 1, Test: test))
                .Where(t => !t.Test.HasOptions && (!string.IsNullOrWhiteSpace(t.Test.AnswerHtml) || !string.IsNullOrEmpty(t.Test.AnswerFile))),
        ];

        /// <summary>
        /// The answer section's descriptive answers (2026-09-24): each one laid out like a question -- its question
        /// number in the same badge, the worked answer as rich text (regular 10pt, formulas as native equations)
        /// and its answer image below -- one unsplittable wrapper row per answer with the same navy separators.
        /// A "Descriptive Answers" heading separates them from the multiple-choice grid when both are present.
        /// </summary>
        private static async Task AppendDescriptiveAnswersAsync(
            Ooxml.Body body, List<(int Number, ExamInformationResponseDto.TestDto Test)> answers, bool withHeading, MainDocumentPart mainPart, Lazy<HttpClient> httpClient)
        {
            if (withHeading)
            {
                var heading = new Ooxml.Paragraph();
                var headingProperties = new Ooxml.ParagraphProperties();
                _ = headingProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "240", After = "160" });
                _ = heading.AppendChild(headingProperties);
                _ = heading.AppendChild(CreateRun("Descriptive Answers", bold: true, colorHex: TextDark, fontSizeHalfPoints: 28));
                _ = body.AppendChild(heading);
            }

            var answerFormat = new ExamWordRichText.RunFormat(false, false, false, false, false, TextDark, 20);
            var answersTable = BuildSharedQuestionTable();
            for (var i = 0; i < answers.Count; i++)
            {
                var (number, test) = answers[i];
                var answerTable = BuildSharedQuestionTable();
                await AppendNumberedTextRowAsync(answerTable, test.AnswerHtml, number, answerFormat, mainPart, httpClient);
                if (!string.IsNullOrEmpty(test.AnswerFile))
                {
                    _ = answerTable.AppendChild(await BuildDescriptiveImageRowAsync(test.AnswerFile, mainPart, httpClient));
                }

                AppendWrappedRowGroup(answersTable, answerTable, includeTrailingBlankRow: i < answers.Count - 1);
            }

            _ = body.AppendChild(answersTable);
        }

        /// <summary>The vertical gap between one row of answer-key blocks and the next. The reference has no
        /// export/styles part of its own to lean on for this, so its true visual gap comes entirely from the
        /// default "Normal" paragraph style (12pt font, 160dxa after-spacing) applied to a plain empty
        /// paragraph between the tables. This export ships no <c>styles.xml</c> part at all, so leaving this
        /// paragraph's formatting unset would fall back to whatever default the renderer itself picks
        /// (LibreOffice/Word disagree) instead of a value we control -- setting the same 12pt run size and
        /// 160dxa after-spacing explicitly reproduces the reference's real gap regardless of renderer.</summary>
        private static Ooxml.Paragraph BuildAnswerKeyRowGapParagraph()
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.SpacingBetweenLines { Before = "0", After = "160" });
            var markRunProperties = new Ooxml.ParagraphMarkRunProperties();
            _ = markRunProperties.AppendChild(new Ooxml.FontSize { Val = AnswerKeyCellFontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });
            _ = paragraphProperties.AppendChild(markRunProperties);
            _ = paragraph.AppendChild(paragraphProperties);
            return paragraph;
        }

        /// <summary>One 10-question mini answer-sheet: a yellow "1 2 3 4" header row, then one row per
        /// question with its number and a filled (correct) or empty square per option. Borders are set
        /// per-cell, not at the table level, to reproduce the reference's exact pattern: a continuous outer
        /// box around the whole block, a divider between the number and option columns on every question
        /// row but not the merged yellow header, and never a line between two option columns or between two
        /// question rows -- see <see cref="AnswerKeyColumnEdges"/>.</summary>
        private static Ooxml.Table BuildAnswerKeyBlock(List<ExamInformationResponseDto.TestDto> tests, int questionStart, int questionEnd)
        {
            var table = new Ooxml.Table();
            var tableProperties = new Ooxml.TableProperties();
            _ = tableProperties.AppendChild(new Ooxml.TableWidth { Width = AnswerKeyBlockContentWidthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = tableProperties.AppendChild(NoTableBorders());
            _ = tableProperties.AppendChild(FixedTableLayout());
            _ = table.AppendChild(tableProperties);
            AppendTableGrid(table, AnswerKeyNumberColumnDxa, AnswerKeyOptionColumnDxa, AnswerKeyOptionColumnDxa, AnswerKeyOptionColumnDxa, AnswerKeyOptionColumnDxa);

            var totalRows = questionEnd - questionStart;

            var headerRow = new Ooxml.TableRow();
            _ = headerRow.AppendChild(AnswerKeyHeaderCell(string.Empty, AnswerKeyNumberColumnDxa, columnIndex: 0, isLastRow: totalRows == 0));
            for (var col = 0; col < 4; col++)
            {
                _ = headerRow.AppendChild(AnswerKeyHeaderCell((col + 1).ToString(CultureInfo.InvariantCulture), AnswerKeyOptionColumnDxa, columnIndex: col + 1, isLastRow: totalRows == 0));
            }

            _ = table.AppendChild(headerRow);

            for (var i = questionStart; i < questionEnd; i++)
            {
                var isLastRow = i == questionEnd - 1;
                var row = new Ooxml.TableRow();
                _ = row.AppendChild(AnswerKeyCell($"{i + 1}", RowGrayBg, bold: true, AnswerKeyNumberColumnDxa, columnIndex: 0, isLastRow: isLastRow));
                var correct = char.ToUpperInvariant(tests[i].CorrectOption ?? ' ');
                var optionLetters = new[] { 'A', 'B', 'C', 'D' };
                for (var col = 0; col < optionLetters.Length; col++)
                {
                    var mark = correct == optionLetters[col] ? "■" : "□";
                    _ = row.AppendChild(AnswerKeyCell(mark, "FFFFFF", bold: false, AnswerKeyOptionColumnDxa, columnIndex: col + 1, isLastRow: isLastRow));
                }

                _ = table.AppendChild(row);
            }

            return table;
        }

        /// <summary>Which sides of an answer-key cell get the number/option-column divider, by 0-based
        /// column index (0 = question number, 1-4 = options A-D): a real border between the number and
        /// option-A columns on every question row, but not the header (whose merged yellow bar has no
        /// internal lines); never between two option columns; always on the block's own outer left (column
        /// 0) and outer right (column 4).</summary>
        private static (bool Left, bool Right) AnswerKeyColumnEdges(int columnIndex, bool isHeaderRow) => columnIndex switch
        {
            0 => (true, !isHeaderRow),
            1 => (!isHeaderRow, false),
            4 => (false, true),
            _ => (false, false),
        };

        private static Ooxml.TableCellBorders AnswerKeyCellBorders(bool top, bool bottom, bool left, bool right)
        {
            var borders = new Ooxml.TableCellBorders();
            _ = borders.AppendChild(top
                ? new Ooxml.TopBorder { Val = Ooxml.BorderValues.Single, Color = AnswerKeyBorderColor, Size = 4 }
                : new Ooxml.TopBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(left
                ? new Ooxml.LeftBorder { Val = Ooxml.BorderValues.Single, Color = AnswerKeyBorderColor, Size = 4 }
                : new Ooxml.LeftBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(bottom
                ? new Ooxml.BottomBorder { Val = Ooxml.BorderValues.Single, Color = AnswerKeyBorderColor, Size = 4 }
                : new Ooxml.BottomBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            _ = borders.AppendChild(right
                ? new Ooxml.RightBorder { Val = Ooxml.BorderValues.Single, Color = AnswerKeyBorderColor, Size = 4 }
                : new Ooxml.RightBorder { Val = Ooxml.BorderValues.None, Size = 0 });
            return borders;
        }

        private static Ooxml.TableCellMargin AnswerKeyCellMargin()
        {
            var margin = new Ooxml.TableCellMargin();
            _ = margin.AppendChild(new Ooxml.LeftMargin { Width = AnswerKeyCellHorizontalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = margin.AppendChild(new Ooxml.RightMargin { Width = AnswerKeyCellHorizontalPaddingDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            return margin;
        }

        private static Ooxml.TableCell AnswerKeyHeaderCell(string text, int widthDxa, int columnIndex, bool isLastRow)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            if (text.Length > 0)
            {
                _ = paragraph.AppendChild(CreateRun(text, bold: true, colorHex: TextDark, fontSizeHalfPoints: AnswerKeyCellFontSizeHalfPoints));
            }

            var (left, right) = AnswerKeyColumnEdges(columnIndex, isHeaderRow: true);
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(AnswerKeyCellBorders(top: true, bottom: isLastRow, left, right));
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = AnswerKeyHeaderYellow });
            _ = cellProperties.AppendChild(AnswerKeyCellMargin());
            _ = cellProperties.AppendChild(new Ooxml.TableCellVerticalAlignment { Val = Ooxml.TableVerticalAlignmentValues.Center });
            _ = cell.AppendChild(cellProperties);
            _ = cell.AppendChild(paragraph);
            return cell;
        }

        private static Ooxml.TableCell AnswerKeyCell(string text, string fillHex, bool bold, int widthDxa, int columnIndex, bool isLastRow)
        {
            var paragraph = new Ooxml.Paragraph();
            var paragraphProperties = new Ooxml.ParagraphProperties();
            _ = paragraphProperties.AppendChild(new Ooxml.Justification { Val = Ooxml.JustificationValues.Center });
            _ = paragraph.AppendChild(paragraphProperties);
            _ = paragraph.AppendChild(CreateRun(text, bold: bold, colorHex: TextDark, fontSizeHalfPoints: AnswerKeyCellFontSizeHalfPoints));

            var (left, right) = AnswerKeyColumnEdges(columnIndex, isHeaderRow: false);
            var cell = new Ooxml.TableCell();
            var cellProperties = new Ooxml.TableCellProperties();
            _ = cellProperties.AppendChild(new Ooxml.TableCellWidth { Width = widthDxa.ToString(CultureInfo.InvariantCulture), Type = Ooxml.TableWidthUnitValues.Dxa });
            _ = cellProperties.AppendChild(AnswerKeyCellBorders(top: false, bottom: isLastRow, left, right));
            _ = cellProperties.AppendChild(new Ooxml.Shading { Val = Ooxml.ShadingPatternValues.Clear, Fill = fillHex });
            _ = cellProperties.AppendChild(AnswerKeyCellMargin());
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
            TPart mainPart, string src, Lazy<HttpClient>? httpClient, int? fixedWidthPx, int? fixedHeightPx, int? maxWidthPx = null)
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

            return EmbedImageBytes(mainPart, bytes, fixedWidthPx, fixedHeightPx, maxWidthPx);
        }

        // maxWidthPx: with neither fixed size given (original size), the widest the picture may be before it's
        // shrunk to fit -- MaxImageWidthPx when null.
        internal static Ooxml.Drawing? EmbedImageBytes<TPart>(TPart mainPart, byte[] bytes, int? fixedWidthPx, int? fixedHeightPx, int? maxWidthPx = null)
            where TPart : OpenXmlPartContainer, ISupportedRelationship<ImagePart>
        {
            var built = BuildImageGraphic(mainPart, bytes, fixedWidthPx, fixedHeightPx, maxWidthPx);
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
            TPart mainPart, byte[] bytes, int? fixedWidthPx, int? fixedHeightPx, int? maxWidthPx)
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
                var widthCap = maxWidthPx ?? MaxImageWidthPx;
                if (widthPx > widthCap)
                {
                    heightPx = (int)Math.Round(heightPx * (widthCap / (double)widthPx));
                    widthPx = widthCap;
                }
            }

            // Always encode the source's own full native pixel data, never resampled down -- widthPx/heightPx
            // above only set the *displayed* size in the document (via widthEmu/heightEmu below), not how
            // much real detail gets embedded. Fixed 2026-09-23: this used to resample down to the display
            // size before encoding (to save file size), but that meant e.g. a shared question-side image
            // measuring 657x154 natively (crisp, legible) got permanently baked down to 150x35 -- unreadable
            // even before Word further upscaled that already-blurred bitmap back up to fill its ~1.6in
            // display box. Word/LibreOffice both scale a picture's embedded bitmap to whatever `w:extent` the
            // drawing declares regardless of the bitmap's own pixel size, so keeping full resolution here
            // costs some .docx file size but never costs sharpness at any zoom/print level.
            var encodeSource = bitmap;

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
            MainDocumentPart mainPart, ExamInformationResponseDto.ExamDto? exam, string? watermarkText, HeaderBrandAssets brandAssets,
            bool googleDocsCompatible)
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
            _ = header.AppendChild(BuildHeaderBackgroundParagraph(TopicsBackgroundExtension(exam)));
            // The Google-Docs-compatible export has its shapes stripped afterwards (GoogleDocsDocxSanitizer), which
            // would take the Header Outline with them -- so there the table keeps its own square outer borders.
            _ = header.AppendChild(await BuildHeaderRowAsync(exam, headerPart, brandAssets, outerBordersFromBackground: !googleDocsCompatible));
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

#pragma warning disable S1075 // the exam footer's own fixed brand website link, not a configurable endpoint
        /// <summary>The footer's own "gamatrain.com" link target -- kept as one constant since it's
        /// referenced both for the hyperlink relationship and (implicitly) the display text below.</summary>
        internal const string GamatrainWebsiteUrl = "https://gamatrain.com";
#pragma warning restore S1075

        /// <summary>
        /// Matches the reference template's exact footer exactly: "{PAGE} / {NUMPAGES}" on the left, the Gama
        /// "G" logo (exam-footer-logo.png; the reference used a globe icon here) plus
        /// "gamatrain.com" centered -- no "Page"/"of" words and no copyright text, both of which the
        /// earlier revision had invented without a reference to match. Unlike the reference (whose own
        /// "www.gamatrain.com" is plain, unlinked text), both the icon and the text are wrapped in one real
        /// <c>w:hyperlink</c> to <see cref="GamatrainWebsiteUrl"/> so the footer is actually clickable --
        /// visual style is left as-is (brand dark, bold, no underline) rather than switching to the default
        /// blue/underlined "Hyperlink" character style, since neither the reference nor the rest of this
        /// export uses that look.
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

            var websiteRelationship = footerPart.AddHyperlinkRelationship(new Uri(GamatrainWebsiteUrl), isExternal: true);
            var hyperlink = new Ooxml.Hyperlink { Id = websiteRelationship.Id, History = true };

            var logoDrawing = EmbedImageBytes(footerPart, brandAssets.FooterLogo, 14, 14);
            if (logoDrawing is not null)
            {
                var logoRun = new Ooxml.Run();
                _ = logoRun.AppendChild(logoDrawing);
                _ = hyperlink.AppendChild(logoRun);
                _ = hyperlink.AppendChild(CreateRun(" ", bold: false, colorHex: TextDark, fontSizeHalfPoints: 16));
            }

            var websiteRun = CreateRun("gamatrain.com", bold: true, colorHex: TextDark, fontSizeHalfPoints: 16);
            _ = hyperlink.AppendChild(websiteRun);

            var siteParagraph = new Ooxml.Paragraph();
            _ = siteParagraph.AppendChild(hyperlink);
            _ = row.AppendChild(BuildBorderlessCell(siteParagraph, Ooxml.JustificationValues.Center, "1667"));

            // An inline picture sits on the text baseline, so the 14px logo would rise above the 8pt text's
            // visual middle. Center-aligning the line's items gets most of the way; the URL is almost all
            // lowercase, whose visual middle sits below the font box's, so it's raised a further 1.5pt.
            _ = siteParagraph.ParagraphProperties!.AppendChild(new Ooxml.TextAlignment { Val = Ooxml.VerticalTextAlignmentValues.Center });
            var websiteRunProperties = websiteRun.RunProperties!;
            _ = websiteRunProperties.InsertBefore(new Ooxml.Position { Val = "3" }, websiteRunProperties.GetFirstChild<Ooxml.FontSize>());

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
