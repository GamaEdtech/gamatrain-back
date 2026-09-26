namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    using AngleSharp.Dom;
    using AngleSharp.Html.Parser;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Data.Dto.Game;

    using SkiaSharp;

    using A = DocumentFormat.OpenXml.Drawing;
    using P = DocumentFormat.OpenXml.Presentation;
    using W = ExamWordDocumentBuilder;

    /// <summary>
    /// Builds an exam PowerPoint deck entirely via native OOXML (PresentationML). Styled like the Word/Pdf exports
    /// (2026-09-25): the Gama brand panel and exam title on every slide, grey number badges, the same options
    /// layouts (<see cref="ExamWordDocumentBuilder.ClassifyLayout"/>), and a footer. One title slide, then one slide
    /// per question; a question that has an answer (correct option or worked answer) also gets a hidden answer
    /// slide right after it, reached from the question slide's "Show Answer" button and left again with its
    /// "Back to Question" button -- hidden, so a normal slideshow still steps from question to question.
    /// </summary>
    internal static partial class ExamPresentationBuilder
    {
        private const long EmuPerPixel = 9525;
        private const long EmuPerPoint = 12700;
        private const int SlideWidth = 12192000; // 16:9, 13.333in
        private const int SlideHeight = 6858000; // 7.5in
        private const long Margin = 457200; // 0.5in

        // Header / footer / content geometry.
        private const long BrandPanelWidth = 2160000;
        private const long BrandPanelHeight = BrandPanelWidth * 104 / 540; // exam-gama-wordmark.png's own aspect
        private const long HeaderTop = 300000;
        private const long HeaderRuleY = HeaderTop + BrandPanelHeight + 150000;
        private const long FooterRuleY = 6240000;
        private const long ContentTop = HeaderRuleY + 200000;
        private const long ContentBottom = FooterRuleY - 150000;
        private const long ContentWidth = SlideWidth - (2 * Margin);

        private const long QuestionBadgeSize = 520000;
        private const long OptionBadgeSize = 400000;
        private const long OptionGap = 120000;
        private const int QuestionFontSize = 2200; // hundredths of a point
        private const int OptionFontSize = 1800;

        private const string Navy = "172437";
        private const string AnswerGreen = "2E7D32";
        private const string AnswerGreenBg = "E8F5E9";
        private const string Yellow = "F6B500";
        private const string NavyMid = "21324A";
        private const string NavyBadge = "202C3E";
        private const string BorderGray = "9AABBA";
        private const string RowGrayBg = W.RowGrayBg;
        private const string TextDark = W.TextDark;
        private const string TextMuted = W.TextMuted;

#pragma warning disable S1075 // spec-mandated namespace URIs, not configurable endpoints
        private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private const string MathNs = "http://schemas.openxmlformats.org/officeDocument/2006/math";
        private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private const string A14Ns = "http://schemas.microsoft.com/office/drawing/2010/main";
#pragma warning restore S1075

        private static uint shapeIdCounter = 1;

        /// <summary>The brand/asset pictures a deck needs.</summary>
        internal sealed record DeckAssets(byte[] BrandPanel, byte[] FooterLogo);

        public static async Task<byte[]> BuildAsync(
            [NotNull] ExamInformationResponseDto data, DeckAssets assets, Lazy<HttpClient> httpClient)
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

                var titleSlidePart = await BuildTitleSlideAsync(presentationPart, slideLayoutPart, data.Exam, assets);
                _ = slideIdList.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(titleSlidePart) });

                var tests = data.Tests ?? [];
                for (var i = 0; i < tests.Count; i++)
                {
                    slideId = await AppendQuestionSlidesAsync(presentationPart, slideLayoutPart, slideIdList, slideId,
                        new SlideContext(data.Exam, assets, i, tests.Count), tests[i], httpClient);
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

            return MakeSlideLinksRelative(stream.ToArray());
        }

        /// <summary>
        /// The SDK writes a slide's relationship to another slide (the Show Answer / Back to Question / More buttons)
        /// with an absolute package path, <c>Target="/ppt/slides/slide3.xml"</c>. PowerPoint copes, but LibreOffice
        /// Impress treats it as an external file ("cannot be passed to an external application", found 2026-09-25) --
        /// so they're rewritten to the relative form PowerPoint itself writes, <c>Target="slide3.xml"</c> (both parts
        /// live in ppt/slides/).
        /// </summary>
        private static byte[] MakeSlideLinksRelative(byte[] package)
        {
            using var stream = new MemoryStream();
            stream.Write(package);
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
            {
                foreach (var entry in zip.Entries.Where(t => t.FullName.StartsWith("ppt/slides/_rels/", StringComparison.Ordinal)).ToList())
                {
                    string xml;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        xml = reader.ReadToEnd();
                    }

                    var relative = xml.Replace("Target=\"/ppt/slides/", "Target=\"", StringComparison.Ordinal);
                    if (relative == xml)
                    {
                        continue;
                    }

                    var name = entry.FullName;
                    entry.Delete();
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(relative);
                }
            }

            return stream.ToArray();
        }

        /// <summary>A question has an answer to show when it has a correct option (multiple choice) or a worked
        /// answer/answer image (descriptive).</summary>
        private static bool HasAnswer(ExamInformationResponseDto.TestDto test) =>
            test.HasOptions ? test.CorrectOption is not null : !string.IsNullOrWhiteSpace(test.AnswerHtml) || !string.IsNullOrEmpty(test.AnswerFile);

        private sealed record SlideContext(ExamInformationResponseDto.ExamDto? Exam, DeckAssets Assets, int Index, int Count);

        private static SlidePart NewSlidePart(PresentationPart presentationPart, SlideLayoutPart slideLayoutPart)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            _ = slidePart.AddPart(slideLayoutPart);
            return slidePart;
        }

        private static void SaveSlide(SlidePart slidePart, P.ShapeTree shapeTree, bool hidden)
        {
            var commonSlideData = new P.CommonSlideData();
            _ = commonSlideData.AppendChild(shapeTree);
            var slide = new P.Slide();
            if (hidden)
            {
                slide.Show = false;
            }

            _ = slide.AppendChild(commonSlideData);
            slidePart.Slide = slide;
            slidePart.Slide.Save();
        }

        // ---- Title slide --------------------------------------------------------------------------------

        private static async Task<SlidePart> BuildTitleSlideAsync(
            PresentationPart presentationPart, SlideLayoutPart slideLayoutPart, ExamInformationResponseDto.ExamDto? exam, DeckAssets assets)
        {
            var slidePart = NewSlidePart(presentationPart, slideLayoutPart);
            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);

            // A light band across the top, the brand panel on it, and the QR code at its right end.
            const long bandHeight = 2300000;
            _ = shapeTree.AppendChild(BuildRectangle(0, 0, SlideWidth, bandHeight, "F2F4F7", null));
            const long panelWidth = 4300000;
            var panel = EmbedPicture(slidePart, assets.BrandPanel, Margin, 700000, panelWidth, panelWidth * 104 / 540);
            if (panel is not null)
            {
                _ = shapeTree.AppendChild(panel.Value.Picture);
            }

            if (!string.IsNullOrEmpty(exam?.QrCode))
            {
                const long qrSize = 1500000;
                var qr = await EmbedPictureFromSourceAsync(slidePart, exam.QrCode, null, SlideWidth - Margin - qrSize, (bandHeight - qrSize) / 2, qrSize, qrSize);
                if (qr is not null)
                {
                    _ = shapeTree.AppendChild(qr.Value.Picture);
                }
            }

            _ = shapeTree.AppendChild(BuildTextBox(Margin, bandHeight + 500000, ContentWidth, 1500000,
                [TextParagraph(exam?.Title ?? string.Empty, 3600, true, Navy)], anchor: A.TextAnchoringTypeValues.Bottom));

            var facts = new List<string> { $"Questions: {exam?.TestsCount}", $"Time: {exam?.ExamTime} min" };
            if (!string.IsNullOrEmpty(exam?.Level))
            {
                facts.Add($"Difficulty Level: {exam.Level}");
            }

            _ = shapeTree.AppendChild(BuildRectangle(Margin, bandHeight + 2150000, 1200000, 60000, Yellow, null));
            _ = shapeTree.AppendChild(BuildTextBox(Margin, bandHeight + 2350000, ContentWidth, 500000,
                [TextParagraph(string.Join("      ", facts), 2000, false, TextDark)]));
            if (!string.IsNullOrEmpty(exam?.Author))
            {
                _ = shapeTree.AppendChild(BuildTextBox(Margin, bandHeight + 2900000, ContentWidth, 500000,
                    [TextParagraph($"By: {exam.Author}", 2000, false, TextMuted)]));
            }

            AppendFooterLink(slidePart, shapeTree, assets);
            SaveSlide(slidePart, shapeTree, hidden: false);
            return slidePart;
        }

        // ---- Question / answer slides --------------------------------------------------------------------

        /// <summary>
        /// One piece of a slide's content -- a question/answer paragraph, a picture, a row of options -- with its height,
        /// so <see cref="Paginate"/> can fill slides from the top and carry whatever doesn't fit onto a continuation slide.
        /// <paramref name="GapBefore"/> is the space above it when it isn't the first block on its slide.
        /// </summary>
        private sealed record Block(long Height, long GapBefore, bool HoldsOptions, Func<SlidePart, P.ShapeTree, long, Task> DrawAsync);

        private sealed record SlideButton(string Label, string FillHex, string RelationshipId);

        private const long PageContentHeight = ContentBottom - ContentTop;
        private const long QuestionTextX = Margin + QuestionBadgeSize + 250000;
        private const long QuestionTextWidth = SlideWidth - Margin - QuestionTextX;
        private const long BlockGap = 250000;

        /// <summary>
        /// Appends one question's slides: its question slide(s), then its hidden answer slide(s) if it has an answer.
        /// Content that doesn't fit one slide continues on the next ("CONTINUED"), at full size -- never shrunk or cut
        /// off (2026-09-25). A multiple-choice question's answer slides mirror the question slides that hold its options
        /// (same layout, correct option highlighted), each reached from its own question slide's "Show Answer" and left
        /// by "Back to Question". A descriptive question's worked answer runs over as many answer slides as it needs,
        /// reached from its last question slide and chained with "More"; "Back to Question" returns to that last slide.
        /// Returns the next free slide id.
        /// </summary>
        private static async Task<uint> AppendQuestionSlidesAsync(PresentationPart presentationPart, SlideLayoutPart slideLayoutPart,
            P.SlideIdList slideIdList, uint slideId, SlideContext context, ExamInformationResponseDto.TestDto test, Lazy<HttpClient> httpClient)
        {
            var images = await LoadImagesAsync(test, httpClient);
            var questionPages = Paginate(BuildQuestionBlocks(test, context.Index, showAnswer: false, images));

            List<List<Block>> answerPages = [];
            List<int> mirroredQuestionPage = [];
            if (HasAnswer(test))
            {
                if (test.HasOptions)
                {
                    // Same blocks and heights, so the same page split; keep only the pages holding the options.
                    var mirrored = Paginate(BuildQuestionBlocks(test, context.Index, showAnswer: true, images));
                    for (var k = 0; k < mirrored.Count; k++)
                    {
                        if (mirrored[k].Any(t => t.HoldsOptions))
                        {
                            answerPages.Add(mirrored[k]);
                            mirroredQuestionPage.Add(k);
                        }
                    }
                }
                else
                {
                    answerPages = Paginate(BuildAnswerBlocks(test, context.Index, images));
                }
            }

            var questionParts = questionPages.Select(_ => NewSlidePart(presentationPart, slideLayoutPart)).ToList();
            var answerParts = answerPages.Select(_ => NewSlidePart(presentationPart, slideLayoutPart)).ToList();
            foreach (var part in questionParts.Concat(answerParts))
            {
                _ = slideIdList.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(part) });
            }

            var questionButtons = questionParts.Select(_ => new List<SlideButton>()).ToList();
            var answerButtons = answerParts.Select(_ => new List<SlideButton>()).ToList();
            for (var k = 0; k < answerParts.Count; k++)
            {
                var questionIndex = test.HasOptions ? mirroredQuestionPage[k] : questionParts.Count - 1;
                var questionPart = questionParts[questionIndex];
                if (test.HasOptions || k == 0)
                {
                    questionButtons[questionIndex].Add(new("Show Answer  ›", AnswerGreen, questionPart.CreateRelationshipToPart(answerParts[k])));
                }

                answerButtons[k].Add(new("‹  Back to Question", Navy, answerParts[k].CreateRelationshipToPart(questionPart)));
                if (!test.HasOptions && k < answerParts.Count - 1)
                {
                    answerButtons[k].Add(new("More  ›", AnswerGreen, answerParts[k].CreateRelationshipToPart(answerParts[k + 1])));
                }
            }

            for (var k = 0; k < questionParts.Count; k++)
            {
                await DrawSlideAsync(questionParts[k], context, questionPages[k], hidden: false, k > 0 ? ("CONTINUED", TextMuted) : null, questionButtons[k]);
            }

            for (var k = 0; k < answerParts.Count; k++)
            {
                await DrawSlideAsync(answerParts[k], context, answerPages[k], hidden: true, ("ANSWER", AnswerGreen), answerButtons[k]);
            }

            return slideId;
        }

        private static async Task DrawSlideAsync(SlidePart slidePart, SlideContext context, List<Block> blocks, bool hidden,
            (string Text, string FillHex)? tag, List<SlideButton> buttons)
        {
            var shapeTree = new P.ShapeTree();
            AppendGroupShapeProperties(shapeTree);
            AppendHeader(slidePart, shapeTree, context, tag);

            var y = ContentTop;
            for (var k = 0; k < blocks.Count; k++)
            {
                if (k > 0)
                {
                    y += blocks[k].GapBefore;
                }

                await blocks[k].DrawAsync(slidePart, shapeTree, y);
                y += blocks[k].Height;
            }

            AppendFooter(slidePart, shapeTree, context, buttons);
            SaveSlide(slidePart, shapeTree, hidden);
        }

        /// <summary>Fills slides from the top with <paramref name="blocks"/>, starting a new slide whenever the next block
        /// doesn't fit the space left; a block taller than a whole slide gets a slide to itself (its text boxes/pictures
        /// are already sized to a slide at most). Always at least one (possibly empty) page.</summary>
        private static List<List<Block>> Paginate(List<Block> blocks)
        {
            var pages = new List<List<Block>> { new() };
            long used = 0;
            foreach (var block in blocks)
            {
                var current = pages[^1];
                var needed = (current.Count > 0 ? block.GapBefore : 0) + block.Height;
                if (current.Count > 0 && used + needed > PageContentHeight)
                {
                    pages.Add([]);
                    current = pages[^1];
                    used = 0;
                    needed = block.Height;
                }

                current.Add(block);
                used += needed;
            }

            return pages;
        }

        /// <summary>The question's paragraphs (the number badge beside the first), then its picture/options.</summary>
        private static List<Block> BuildQuestionBlocks(ExamInformationResponseDto.TestDto test, int index, bool showAnswer, Dictionary<string, byte[]> images)
        {
            var blocks = TextBlocks(test.Question, QuestionFontSize, bold: true, TextDark, null);
            if (!test.HasOptions)
            {
                AddPictureBlock(blocks, test.QuestionFile, images, QuestionTextX, QuestionTextWidth, PageContentHeight);
            }
            else
            {
                AddOptionBlocks(blocks, test, showAnswer, images);
            }

            return WithNumberBadge(blocks, index);
        }

        /// <summary>A descriptive question's worked answer, paragraph by paragraph on a light green panel (the number badge
        /// beside the first), then its answer image.</summary>
        private static List<Block> BuildAnswerBlocks(ExamInformationResponseDto.TestDto test, int index, Dictionary<string, byte[]> images)
        {
            var blocks = TextBlocks(test.AnswerHtml, OptionFontSize, bold: false, TextDark, AnswerGreenBg);
            AddPictureBlock(blocks, test.AnswerFile, images, QuestionTextX, QuestionTextWidth, PageContentHeight);
            return WithNumberBadge(blocks, index);
        }

        private static List<Block> WithNumberBadge(List<Block> blocks, int index)
        {
            if (blocks.Count == 0)
            {
                blocks.Add(new Block(QuestionBadgeSize, 0, false, (_, _, _) => Task.CompletedTask));
            }

            var first = blocks[0];
            blocks[0] = first with
            {
                Height = Math.Max(first.Height, QuestionBadgeSize),
                DrawAsync = async (slidePart, shapeTree, y) =>
                {
                    _ = shapeTree.AppendChild(BuildChip(Margin, y, QuestionBadgeSize, QuestionBadgeSize,
                        (index + 1).ToString(CultureInfo.InvariantCulture), 2000, W.BadgeGray, TextDark));
                    await first.DrawAsync(slidePart, shapeTree, y);
                },
            };
            return blocks;
        }

        /// <summary>One block per top-level paragraph of <paramref name="html"/>, so a long text can continue on the next
        /// slide between paragraphs. With <paramref name="panelHex"/>, each block also paints its own slice of a
        /// background panel (touching slices, so consecutive paragraphs read as one panel).</summary>
        private static List<Block> TextBlocks(string? html, int fontSize, bool bold, string colorHex, string? panelHex)
        {
            var inset = panelHex is null ? 0 : 150000;
            var paragraphs = SplitParagraphs(html);
            var blocks = new List<Block>();
            for (var k = 0; k < paragraphs.Count; k++)
            {
                var paragraph = paragraphs[k];
                var padTop = panelHex is not null && k == 0 ? 100000 : 0;
                var padBottom = panelHex is not null && k == paragraphs.Count - 1 ? 100000 : 0;
                var textHeight = Math.Min(PageContentHeight - padTop - padBottom, EstimateTextHeight(paragraph, fontSize, QuestionTextWidth - (2 * inset)));
                blocks.Add(new Block(textHeight + padTop + padBottom, 0, false, (slidePart, shapeTree, y) =>
                {
                    if (panelHex is not null)
                    {
                        _ = shapeTree.AppendChild(BuildRectangle(QuestionTextX, y, QuestionTextWidth, textHeight + padTop + padBottom, panelHex, null));
                    }

                    _ = shapeTree.AppendChild(BuildTextBox(QuestionTextX + inset, y + padTop, QuestionTextWidth - (2 * inset), textHeight,
                        BuildRichParagraphs(paragraph, fontSize, bold, colorHex)));
                    return Task.CompletedTask;
                }));
            }

            return blocks;
        }

        /// <summary>The top-level paragraphs of a rich-text fragment (each &lt;p&gt;/&lt;div&gt;, or the whole fragment if it
        /// has none), each still as HTML for <see cref="BuildRichParagraphs"/>.</summary>
        private static List<string> SplitParagraphs(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var body = new HtmlParser().ParseDocument($"<body>{html}</body>").Body!;
            var blocks = body.Children.Where(t => t.NodeName is "P" or "DIV").ToList();
            return blocks.Count == 0 ? [html] : [.. blocks.Select(t => t.OuterHtml)];
        }

        private static void AddPictureBlock(List<Block> blocks, string? src, Dictionary<string, byte[]> images, long x, long maxWidth, long maxHeight)
        {
            if (string.IsNullOrEmpty(src) || !images.TryGetValue(src, out var bytes) || FitPicture(bytes, maxWidth, maxHeight) is not { } size)
            {
                return;
            }

            blocks.Add(new Block(size.Height, BlockGap, false, (slidePart, shapeTree, y) =>
            {
                if (EmbedPicture(slidePart, bytes, x, y, maxWidth, maxHeight, centerHorizontally: true) is { } picture)
                {
                    _ = shapeTree.AppendChild(picture.Picture);
                }

                return Task.CompletedTask;
            }));
        }

        /// <summary>
        /// The options, in the same arrangement the Word/Pdf exports use for this question
        /// (<see cref="ExamWordDocumentBuilder.ClassifyLayout"/>): 4 across, 2x2, stacked, or 4 image options across --
        /// one block per row, so a long list can continue on the next slide. A shared question image goes above 4-across
        /// options, or to the right of 2x2/stacked options (then image and options are one block).
        /// </summary>
        private static void AddOptionBlocks(List<Block> blocks, ExamInformationResponseDto.TestDto test, bool showAnswer, Dictionary<string, byte[]> images)
        {
            var options = W.GetOptionModels(test);
            var layout = W.ClassifyLayout(test, options);
            var columns = layout switch
            {
                W.QuestionLayoutType.TextVertical => 1,
                W.QuestionLayoutType.Text2x2 => 2,
                _ => 4,
            };
            var imageOptions = layout == W.QuestionLayoutType.ImageOptionsHorizontal;
            var questionImage = imageOptions ? null : test.QuestionFile;
            var correctNumber = showAnswer && test.CorrectOption is { } correct ? (correct - 'A' + 1).ToString(CultureInfo.InvariantCulture) : null;

            byte[]? sideImage = null;
            (long Width, long Height)? sideImageSize = null;
            var optionsWidth = QuestionTextWidth;
            var sideImageWidth = QuestionTextWidth * 38 / 100;
            if (columns != 4 && !string.IsNullOrEmpty(questionImage) && images.TryGetValue(questionImage, out var questionImageBytes))
            {
                sideImage = questionImageBytes;
                sideImageSize = FitPicture(questionImageBytes, sideImageWidth, PageContentHeight);
                optionsWidth = QuestionTextWidth - sideImageWidth - BlockGap;
            }

            var cellWidth = (optionsWidth - ((columns - 1) * OptionGap)) / columns;
            var contentWidth = cellWidth - OptionBadgeSize - 150000;
            var rows = new List<(OptionModelRow Row, long Height)>();
            for (var r = 0; r * columns < options.Length; r++)
            {
                var row = new OptionModelRow([.. options.Skip(r * columns).Take(columns)]);
                var height = imageOptions
                    ? Math.Max(OptionBadgeSize, row.Options.Max(o => o.File is not null && images.TryGetValue(o.File, out var b) ? FitPicture(b, contentWidth, PageContentHeight)?.Height ?? 0 : 0))
                    : Math.Max(OptionBadgeSize + 100000, row.Options.Max(o => EstimateTextHeight(o.Html, OptionFontSize, contentWidth)) + 100000);
                rows.Add((row, height));
            }

            Task DrawRowAsync(SlidePart slidePart, P.ShapeTree shapeTree, OptionModelRow row, long y, long height)
            {
                for (var c = 0; c < row.Options.Length; c++)
                {
                    var option = row.Options[c];
                    var cellX = QuestionTextX + (c * (cellWidth + OptionGap));
                    var isCorrect = option.Number == correctNumber;
                    if (isCorrect)
                    {
                        _ = shapeTree.AppendChild(BuildRectangle(cellX - 60000, y - 40000, cellWidth + 120000, height + 80000, AnswerGreenBg, AnswerGreen));
                    }

                    var badgeY = imageOptions ? y : y + ((height - OptionBadgeSize) / 2);
                    _ = shapeTree.AppendChild(BuildChip(cellX, badgeY, OptionBadgeSize, OptionBadgeSize, option.Number, 1600,
                        isCorrect ? AnswerGreen : W.BadgeGray, isCorrect ? "FFFFFF" : TextDark));

                    var contentX = cellX + OptionBadgeSize + 150000;
                    if (!string.IsNullOrWhiteSpace(option.Html) || option.File is null || !images.TryGetValue(option.File, out var optionImage))
                    {
                        _ = shapeTree.AppendChild(BuildTextBox(contentX, y, contentWidth, height,
                            BuildRichParagraphs(option.Html, OptionFontSize, isCorrect, isCorrect ? AnswerGreen : TextDark),
                            anchor: A.TextAnchoringTypeValues.Center));
                    }
                    else if (EmbedPicture(slidePart, optionImage, contentX, y, contentWidth, height) is { } picture)
                    {
                        _ = shapeTree.AppendChild(picture.Picture);
                    }
                }

                return Task.CompletedTask;
            }

            if (columns == 4 && !string.IsNullOrEmpty(questionImage))
            {
                // A picture above 4-across options gets the height left once the question text and the options row are
                // placed -- so the options stay on the same slide instead of being pushed alone onto a continuation --
                // but never less than 35% of a slide; only then does the content continue on the next slide.
                // The first text block is later raised to at least the number badge's height (WithNumberBadge).
                var textHeight = blocks.Count == 0 ? QuestionBadgeSize : blocks.Sum(t => t.Height) - blocks[0].Height + Math.Max(blocks[0].Height, QuestionBadgeSize);
                var optionsHeight = rows.Sum(t => t.Height) + ((rows.Count - 1) * OptionGap);
                var room = PageContentHeight - textHeight - optionsHeight - (2 * BlockGap) - 20000;
                AddPictureBlock(blocks, questionImage, images, QuestionTextX, QuestionTextWidth, Math.Max(room, PageContentHeight * 35 / 100));
            }

            if (sideImage is not null && sideImageSize is { } imageSize)
            {
                // Options + the shared image beside them: one block, as tall as the taller of the two.
                var optionsHeight = rows.Sum(t => t.Height) + ((rows.Count - 1) * OptionGap);
                blocks.Add(new Block(Math.Max(optionsHeight, imageSize.Height), BlockGap, true, async (slidePart, shapeTree, y) =>
                {
                    var rowY = y;
                    foreach (var (row, height) in rows)
                    {
                        await DrawRowAsync(slidePart, shapeTree, row, rowY, height);
                        rowY += height + OptionGap;
                    }

                    if (EmbedPicture(slidePart, sideImage, QuestionTextX + QuestionTextWidth - sideImageWidth, y, sideImageWidth, PageContentHeight, centerHorizontally: true) is { } picture)
                    {
                        _ = shapeTree.AppendChild(picture.Picture);
                    }
                }));
                return;
            }

            for (var r = 0; r < rows.Count; r++)
            {
                var (row, height) = rows[r];
                blocks.Add(new Block(height, r == 0 ? BlockGap : OptionGap, true, (slidePart, shapeTree, y) => DrawRowAsync(slidePart, shapeTree, row, y, height)));
            }
        }

        private sealed record OptionModelRow(W.OptionModel[] Options);

        /// <summary>Downloads every picture the question's slides may show, once (question, option and answer images).</summary>
        private static async Task<Dictionary<string, byte[]>> LoadImagesAsync(ExamInformationResponseDto.TestDto test, Lazy<HttpClient> httpClient)
        {
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var src in new[] { test.QuestionFile, test.AnswerFile, test.OptionAFile, test.OptionBFile, test.OptionCFile, test.OptionDFile })
            {
                if (!string.IsNullOrEmpty(src) && !images.ContainsKey(src) && await LoadImageBytesAsync(src, httpClient) is { } bytes)
                {
                    images[src] = bytes;
                }
            }

            return images;
        }

        private static async Task<byte[]?> LoadImageBytesAsync(string src, Lazy<HttpClient>? httpClient)
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',', StringComparison.Ordinal);
                return comma < 0 ? null : Convert.FromBase64String(src[(comma + 1)..]);
            }

            if (httpClient is null || !Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
            {
                return null;
            }

            try
            {
                return await httpClient.Value.GetByteArrayAsync(absoluteUri);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>The size a picture is shown at (see <see cref="EmbedPicture"/>): original size, shrunk proportionally
        /// to fit, never enlarged. <see langword="null"/> if the bytes aren't a readable image.</summary>
        private static (long Width, long Height)? FitPicture(byte[] bytes, long maxWidth, long maxHeight)
        {
            var info = SKBitmap.DecodeBounds(bytes);
            if (info.Width <= 0 || info.Height <= 0)
            {
                return null;
            }

            var width = (double)info.Width * EmuPerPixel;
            var height = (double)info.Height * EmuPerPixel;
            var scale = Math.Min(1d, Math.Min(maxWidth / width, maxHeight / height));
            return ((long)(width * scale), (long)(height * scale));
        }

        // ---- Header / footer ------------------------------------------------------------------------------

        private static void AppendHeader(SlidePart slidePart, P.ShapeTree shapeTree, SlideContext context, (string Text, string FillHex)? tag)
        {
            var panel = EmbedPicture(slidePart, context.Assets.BrandPanel, Margin, HeaderTop, BrandPanelWidth, BrandPanelHeight);
            if (panel is not null)
            {
                _ = shapeTree.AppendChild(panel.Value.Picture);
            }

            var titleX = Margin + BrandPanelWidth + 300000;
            var tagWidth = tag is null ? 0 : 1500000;
            _ = shapeTree.AppendChild(BuildTextBox(titleX, HeaderTop, SlideWidth - Margin - titleX - tagWidth - (tag is null ? 0 : 200000), BrandPanelHeight,
                [TextParagraph(context.Exam?.Title ?? string.Empty, 1400, true, Navy)], anchor: A.TextAnchoringTypeValues.Center));
            if (tag is { } label)
            {
                _ = shapeTree.AppendChild(BuildChip(SlideWidth - Margin - tagWidth, HeaderTop + ((BrandPanelHeight - 380000) / 2), tagWidth, 380000,
                    label.Text, 1400, label.FillHex, "FFFFFF", rounded: true));
            }

            _ = shapeTree.AppendChild(BuildRectangle(Margin, HeaderRuleY, ContentWidth, 12700, W.BorderLightGray, null));
        }

        /// <summary>The footer: "question n / total" on the left, the website link centered, and the slide's buttons
        /// right-aligned (rightmost first).</summary>
        private static void AppendFooter(SlidePart slidePart, P.ShapeTree shapeTree, SlideContext context, List<SlideButton> buttons)
        {
            AppendFooterLink(slidePart, shapeTree, context.Assets);
            _ = shapeTree.AppendChild(BuildTextBox(Margin, FooterRuleY + 60000, 2500000, 420000,
                [TextParagraph($"{context.Index + 1} / {context.Count}", 1400, false, TextMuted)], anchor: A.TextAnchoringTypeValues.Center));

            const long buttonWidth = 2300000;
            var right = SlideWidth - Margin;
            foreach (var button in buttons)
            {
                _ = shapeTree.AppendChild(BuildChip(right - buttonWidth, FooterRuleY + 80000, buttonWidth, 400000,
                    button.Label, 1400, button.FillHex, "FFFFFF", rounded: true, hyperlinkRelationshipId: button.RelationshipId));
                right -= buttonWidth + 150000;
            }
        }

        /// <summary>The footer rule and the centered Gama logo + "gamatrain.com" link (a real hyperlink).</summary>
        private static void AppendFooterLink(SlidePart slidePart, P.ShapeTree shapeTree, DeckAssets assets)
        {
            _ = shapeTree.AppendChild(BuildRectangle(Margin, FooterRuleY, ContentWidth, 12700, W.BorderLightGray, null));
            const long linkWidth = 2600000;
            const long logoSize = 200000;
            var linkX = (SlideWidth - linkWidth) / 2;
            var logo = EmbedPicture(slidePart, assets.FooterLogo, linkX, FooterRuleY + 170000, logoSize, logoSize);
            if (logo is not null)
            {
                _ = shapeTree.AppendChild(logo.Value.Picture);
            }

            var websiteRelationship = slidePart.AddHyperlinkRelationship(new Uri(W.GamatrainWebsiteUrl), isExternal: true);
            _ = shapeTree.AppendChild(BuildTextBox(linkX + logoSize + 80000, FooterRuleY + 60000, linkWidth - logoSize - 80000, 420000,
                [TextParagraph("gamatrain.com", 1400, true, TextDark)], anchor: A.TextAnchoringTypeValues.Center,
                hyperlinkRelationshipId: websiteRelationship.Id, externalLink: true));
        }

        // ---- Shapes ---------------------------------------------------------------------------------------

        private static P.Shape BuildRectangle(long x, long y, long cx, long cy, string fillHex, string? outlineHex) =>
            BuildShape(x, y, cx, cy, A.ShapeTypeValues.Rectangle, fillHex, outlineHex, null, A.TextAnchoringTypeValues.Top, null, false);

        /// <summary>A filled box with centered text: the number badges, the "ANSWER" tag and the answer buttons.
        /// With <paramref name="hyperlinkRelationshipId"/>, clicking the whole shape jumps to that slide.</summary>
        private static P.Shape BuildChip(long x, long y, long cx, long cy, string text, int fontSize, string fillHex, string textHex,
            bool rounded = false, string? hyperlinkRelationshipId = null)
        {
            var paragraph = TextParagraph(text, fontSize, true, textHex);
            _ = paragraph.PrependChild(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center });
            return BuildShape(x, y, cx, cy, rounded ? A.ShapeTypeValues.RoundRectangle : A.ShapeTypeValues.Rectangle, fillHex, null,
                [paragraph], A.TextAnchoringTypeValues.Center, hyperlinkRelationshipId, false);
        }

        private static P.Shape BuildTextBox(long x, long y, long cx, long cy, List<A.Paragraph> paragraphs,
            A.TextAnchoringTypeValues? anchor = null, string? hyperlinkRelationshipId = null, bool externalLink = false) =>
            BuildShape(x, y, cx, cy, A.ShapeTypeValues.Rectangle, null, null, paragraphs, anchor ?? A.TextAnchoringTypeValues.Top, hyperlinkRelationshipId, externalLink);

        private static P.Shape BuildShape(long x, long y, long cx, long cy, A.ShapeTypeValues preset, string? fillHex, string? outlineHex,
            List<A.Paragraph>? paragraphs, A.TextAnchoringTypeValues anchor, string? hyperlinkRelationshipId, bool externalLink)
        {
            var nonVisualDrawingProperties = new P.NonVisualDrawingProperties { Id = NextShapeId(), Name = paragraphs is null ? "Rectangle" : "TextBox" };
            if (hyperlinkRelationshipId is not null)
            {
                // A slide jump (the answer buttons) or an external link (the website); on the shape itself, so the
                // whole box is clickable and its text isn't restyled as a hyperlink.
                _ = nonVisualDrawingProperties.AppendChild(externalLink
                    ? new A.HyperlinkOnClick { Id = hyperlinkRelationshipId }
                    : new A.HyperlinkOnClick { Id = hyperlinkRelationshipId, Action = "ppaction://hlinksldjump" });
            }

            var nonVisualShapeProperties = new P.NonVisualShapeProperties();
            _ = nonVisualShapeProperties.AppendChild(nonVisualDrawingProperties);
            _ = nonVisualShapeProperties.AppendChild(new P.NonVisualShapeDrawingProperties { TextBox = paragraphs is not null && fillHex is null });
            _ = nonVisualShapeProperties.AppendChild(new P.ApplicationNonVisualDrawingProperties());

            var shape = new P.Shape();
            _ = shape.AppendChild(nonVisualShapeProperties);

            var transform2D = new A.Transform2D();
            _ = transform2D.AppendChild(new A.Offset { X = x, Y = y });
            _ = transform2D.AppendChild(new A.Extents { Cx = Math.Max(1, cx), Cy = Math.Max(1, cy) });

            var presetGeometry = new A.PresetGeometry { Preset = preset };
            _ = presetGeometry.AppendChild(new A.AdjustValueList());

            var shapeProperties = new P.ShapeProperties();
            _ = shapeProperties.AppendChild(transform2D);
            _ = shapeProperties.AppendChild(presetGeometry);
            if (fillHex is null)
            {
                _ = shapeProperties.AppendChild(new A.NoFill());
            }
            else
            {
                var fill = new A.SolidFill();
                _ = fill.AppendChild(new A.RgbColorModelHex { Val = fillHex });
                _ = shapeProperties.AppendChild(fill);
            }

            var outline = new A.Outline { Width = outlineHex is null ? null : 19050 };
            if (outlineHex is null)
            {
                _ = outline.AppendChild(new A.NoFill());
            }
            else
            {
                var outlineFill = new A.SolidFill();
                _ = outlineFill.AppendChild(new A.RgbColorModelHex { Val = outlineHex });
                _ = outline.AppendChild(outlineFill);
            }

            _ = shapeProperties.AppendChild(outline);
            _ = shape.AppendChild(shapeProperties);

            if (paragraphs is not null)
            {
                // p:sp requires PresentationML's own p:txBody wrapper here, not DrawingML's a:txBody.
                var textBody = new P.TextBody();
                _ = textBody.AppendChild(new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = anchor, LeftInset = 45720, RightInset = 45720, TopInset = 22860, BottomInset = 22860 });
                _ = textBody.AppendChild(new A.ListStyle());
                foreach (var paragraph in paragraphs)
                {
                    _ = textBody.AppendChild(paragraph);
                }

                _ = shape.AppendChild(textBody);
            }

            return shape;
        }

        private static A.Paragraph TextParagraph(string text, int fontSize, bool bold, string colorHex)
        {
            var paragraph = new A.Paragraph();
            _ = paragraph.AppendChild(new A.Run(RunProperties(fontSize, bold, false, false, 0, colorHex), new A.Text(text)));
            return paragraph;
        }

        private static A.RunProperties RunProperties(int fontSize, bool bold, bool italic, bool underline, int baseline, string colorHex)
        {
            var runProperties = new A.RunProperties { Language = "en-US", FontSize = fontSize, Bold = bold, Dirty = false };
            if (italic)
            {
                runProperties.Italic = true;
            }

            if (underline)
            {
                runProperties.Underline = A.TextUnderlineValues.Single;
            }

            if (baseline != 0)
            {
                runProperties.Baseline = baseline;
            }

            var fill = new A.SolidFill();
            _ = fill.AppendChild(new A.RgbColorModelHex { Val = colorHex });
            _ = runProperties.AppendChild(fill);
            return runProperties;
        }

        // ---- Rich text ------------------------------------------------------------------------------------

        private readonly record struct RunStyle(bool Bold, bool Italic, bool Underline, int Baseline, string ColorHex);

        /// <summary>
        /// Converts Core's rich-text HTML into DrawingML paragraphs the same way <see cref="ExamWordRichText"/> does
        /// for Word: each top-level &lt;p&gt;/&lt;div&gt; is one paragraph (with none, the whole fragment is one);
        /// line breaks, bold/italic/underline, superscript/subscript and a CSS text color are kept. A formula (a
        /// <c>data-omml-b64</c> marker from <c>RenderFormulasToOmmlAsync</c>) stays inline in its sentence as a
        /// native PowerPoint equation (<see cref="EquationXml"/>); a paragraph holding only formulas becomes
        /// one left-aligned display equation, since Office centers a lone equation otherwise.
        /// </summary>
        private static List<A.Paragraph> BuildRichParagraphs(string? html, int fontSize, bool bold, string colorHex)
        {
            var baseStyle = new RunStyle(bold, false, false, 0, colorHex);
            if (string.IsNullOrWhiteSpace(html))
            {
                var empty = new A.Paragraph();
                _ = empty.AppendChild(new A.EndParagraphRunProperties { Language = "en-US", FontSize = fontSize });
                return [empty];
            }

            var body = new HtmlParser().ParseDocument($"<body>{html}</body>").Body!;
            var blocks = body.Children.Where(t => t.NodeName is "P" or "DIV").ToList();
            var sources = blocks.Count == 0 ? [body.ChildNodes] : blocks.Select(t => t.ChildNodes).ToList();

            var paragraphs = new List<A.Paragraph>();
            foreach (var nodes in sources)
            {
                var parts = new List<(string Xml, bool IsMath, bool IsText, RunStyle Style)>();
                foreach (var node in nodes)
                {
                    CollectParts(node, baseStyle, fontSize, parts);
                }

                paragraphs.Add(BuildParagraph(parts, fontSize));
            }

            return paragraphs;
        }

        private static void CollectParts(INode node, RunStyle style, int fontSize, List<(string Xml, bool IsMath, bool IsText, RunStyle Style)> parts)
        {
            if (node.NodeType == NodeType.Text)
            {
                var text = node.TextContent;
                if (text.Length > 0)
                {
                    parts.Add((RunXml(text, style, fontSize), false, !string.IsNullOrWhiteSpace(text), style));
                }

                return;
            }

            if (node is not IElement element)
            {
                return;
            }

            switch (element.NodeName)
            {
                case "BR":
                    parts.Add(($"<a:br><a:rPr lang=\"en-US\" sz=\"{fontSize}\" dirty=\"0\"/></a:br>", false, false, style));
                    return;
                case "SPAN" when element.HasAttribute("data-omml-b64"):
                    var equation = OmmlForPowerPoint(element.GetAttribute("data-omml-b64"), style, fontSize);
                    if (equation is not null)
                    {
                        parts.Add((equation, true, false, style));
                    }

                    return;
                case "B" or "STRONG":
                    style = style with { Bold = true };
                    break;
                case "I" or "EM":
                    style = style with { Italic = true };
                    break;
                case "U":
                    style = style with { Underline = true };
                    break;
                case "SUP":
                    style = style with { Baseline = 30000 };
                    break;
                case "SUB":
                    style = style with { Baseline = -25000 };
                    break;
                case "SPAN" or "P" or "DIV":
                    if (ExamWordRichText.ExtractCssColor(element.GetAttribute("style")) is { } color)
                    {
                        style = style with { ColorHex = color };
                    }

                    break;
                default:
                    break;
            }

            foreach (var child in element.ChildNodes)
            {
                CollectParts(child, style, fontSize, parts);
            }
        }

        private static string RunXml(string text, RunStyle style, int fontSize) =>
            string.Create(CultureInfo.InvariantCulture,
                $"<a:r><a:rPr lang=\"en-US\" sz=\"{fontSize}\" b=\"{(style.Bold ? 1 : 0)}\"{(style.Italic ? " i=\"1\"" : string.Empty)}{(style.Underline ? " u=\"sng\"" : string.Empty)}{(style.Baseline != 0 ? $" baseline=\"{style.Baseline}\"" : string.Empty)} dirty=\"0\"><a:solidFill><a:srgbClr val=\"{style.ColorHex}\"/></a:solidFill></a:rPr><a:t>{System.Security.SecurityElement.Escape(text)}</a:t></a:r>");

        private static A.Paragraph BuildParagraph(List<(string Xml, bool IsMath, bool IsText, RunStyle Style)> parts, int fontSize)
        {
            var onlyEquations = parts.Any(t => t.IsMath) && !parts.Any(t => t.IsText);
            var content = new StringBuilder();
            if (onlyEquations)
            {
                // One left-aligned display equation holding every formula of the paragraph.
                _ = content.Append("<a:pPr algn=\"l\"/>").Append(EquationXml(
                    $"<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>{string.Concat(parts.Where(t => t.IsMath).Select(t => t.Xml))}</m:oMathPara>",
                    parts.First(t => t.IsMath).Style, fontSize));
            }
            else
            {
                foreach (var (xml, isMath, _, style) in parts)
                {
                    _ = content.Append(isMath ? EquationXml(xml, style, fontSize) : xml);
                }
            }

            _ = content.Append(CultureInfo.InvariantCulture, $"<a:endParaRPr lang=\"en-US\" sz=\"{fontSize}\" dirty=\"0\"/>");
            return new A.Paragraph($"<a:p xmlns:a=\"{DrawingNs}\">{content}</a:p>");
        }

        /// <summary>
        /// Wraps equation content (an <c>m:oMath</c>, or an <c>m:oMathPara</c>) in the markup PowerPoint 2010+ uses
        /// for equations in slide text -- <c>mc:AlternateContent</c>/<c>a14:m</c>; DrawingML's <c>a:p</c> has no
        /// direct slot for OMML -- with a formatted text version as the fallback (<see cref="FallbackRuns"/>).
        /// </summary>
        private static string EquationXml(string ommlContent, RunStyle style, int fontSize)
        {
            var fallback = string.Concat(FallbackRuns(XElement.Parse($"<x xmlns:m=\"{MathNs}\" xmlns:a=\"{DrawingNs}\">{ommlContent}</x>"), 0)
                .Select(t => RunXml(t.Text, style with { Baseline = t.Baseline }, fontSize)));
            // Namespaces declared exactly where PowerPoint itself declares them -- mc on AlternateContent, a14 on the
            // Choice, m on the math element. PowerPoint's markup-compatibility check resolves Choice's Requires="a14"
            // strictly: declared only on the enclosing a:p, it wasn't recognized, and PowerPoint showed the plain-text
            // Fallback instead of the equation (found live 2026-09-25, exam 831's "e^(−6)×(6^5)/(5!)").
            var mathStart = ommlContent.IndexOf('>', StringComparison.Ordinal);
            var selfClosing = mathStart > 0 && ommlContent[mathStart - 1] == '/';
            var mathTagEnd = selfClosing ? mathStart - 1 : mathStart;
            ommlContent = ommlContent[..mathTagEnd] + $" xmlns:m=\"{MathNs}\"" + ommlContent[mathTagEnd..];
            return $"<mc:AlternateContent xmlns:mc=\"{McNs}\"><mc:Choice xmlns:a14=\"{A14Ns}\" Requires=\"a14\"><a14:m>{ommlContent}</a14:m></mc:Choice>" +
                $"<mc:Fallback>{fallback}</mc:Fallback></mc:AlternateContent>";
        }

        /// <summary>
        /// The equation as formatted text runs, for viewers that can't draw PowerPoint equations -- LibreOffice Impress
        /// and (likely) Google Slides show the <c>mc:Fallback</c> instead: powers and indices as real superscript/
        /// subscript text (a run <c>baseline</c>), fractions as <c>(a)/(b)</c> and roots as <c>√(...)</c> (plain text
        /// can't stack them), delimiters and operators kept. Adjacent runs at the same baseline are merged.
        /// </summary>
        private static List<(string Text, int Baseline)> FallbackRuns(XElement element, int baseline)
        {
            const int superscriptBaseline = 30000;
            const int subscriptBaseline = -25000;
            XNamespace m = MathNs;
            List<(string Text, int Baseline)> Part(string name, int level) => element.Element(m + name) is { } child ? FallbackRuns(child, level) : [];
            List<(string Text, int Baseline)> Children() => [.. element.Elements().SelectMany(t => FallbackRuns(t, baseline))];
            List<(string Text, int Baseline)> Text(string text) => text.Length == 0 ? [] : [(text, baseline)];
            static List<(string Text, int Baseline)> Grouped(List<(string Text, int Baseline)> runs, int level) =>
                runs.Sum(t => t.Text.Length) <= 1 ? runs : [("(", level), .. runs, (")", level)];
            string Attribute(string property, string fallback) =>
                element.Element(m + (element.Name.LocalName + "Pr"))?.Element(m + property)?.Attribute(m + "val")?.Value ?? fallback;

            var runs = element.Name.Namespace != m ? Children() : element.Name.LocalName switch
            {
                "t" => Text(element.Value),
                "rPr" or "ctrlPr" or "fPr" or "sSupPr" or "sSubPr" or "sSubSupPr" or "radPr" or "dPr" or "naryPr" or "funcPr" or "oMathParaPr" => [],
                "f" => [.. Grouped(Part("num", baseline), baseline), ("/", baseline), .. Grouped(Part("den", baseline), baseline)],
                "sSup" => [.. Part("e", baseline), .. Part("sup", superscriptBaseline)],
                "sSub" => [.. Part("e", baseline), .. Part("sub", subscriptBaseline)],
                "sSubSup" => [.. Part("e", baseline), .. Part("sub", subscriptBaseline), .. Part("sup", superscriptBaseline)],
                "sPre" => [.. Part("sub", subscriptBaseline), .. Part("sup", superscriptBaseline), .. Part("e", baseline)],
                "rad" => [.. Part("deg", superscriptBaseline), ("√(", baseline), .. Part("e", baseline), (")", baseline)],
                "d" => [(Attribute("begChr", "("), baseline),
                    .. element.Elements(m + "e").SelectMany((e, k) => k == 0 ? FallbackRuns(e, baseline) : [(Attribute("sepChr", ","), baseline), .. FallbackRuns(e, baseline)]),
                    (Attribute("endChr", ")"), baseline)],
                "nary" => [(Attribute("chr", "∫"), baseline), .. Part("sub", subscriptBaseline), .. Part("sup", superscriptBaseline), (" ", baseline), .. Part("e", baseline)],
                "func" => [.. Part("fName", baseline), (" ", baseline), .. Part("e", baseline)],
                "oMath" when element.Parent?.Name == m + "oMathPara" => [.. Children(), (" ", baseline)],
                _ => Children(),
            };

            var merged = new List<(string Text, int Baseline)>();
            foreach (var run in runs.Where(t => t.Text.Length > 0))
            {
                if (merged.Count > 0 && merged[^1].Baseline == run.Baseline)
                {
                    merged[^1] = (merged[^1].Text + run.Text, run.Baseline);
                }
                else
                {
                    merged.Add(run);
                }
            }

            return merged;
        }

        /// <summary>
        /// Decodes a formula marker's OMML (produced for Word) and adapts it for PowerPoint: Word's run formatting
        /// (<c>w:rPr</c>) means nothing inside a slide, so it's replaced on every math run by DrawingML's own
        /// <c>a:rPr</c> -- the text size, color and bold of the surrounding sentence, in Cambria Math -- which is how
        /// PowerPoint itself stores equation text. Returns the bare <c>m:oMath</c> element's XML (namespace
        /// declarations left to the enclosing paragraph), or <see langword="null"/> for malformed input: one bad
        /// formula is dropped rather than risking a corrupt slide.
        /// </summary>
        private static string? OmmlForPowerPoint(string? base64, RunStyle style, int fontSize)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }

            try
            {
                var math = XElement.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
                XNamespace m = MathNs;
                XNamespace w = WordNs;
                XNamespace a = DrawingNs;
                foreach (var wordRunProperties in math.Descendants(w + "rPr").ToList())
                {
                    wordRunProperties.Remove();
                }

                foreach (var run in math.Descendants(m + "r"))
                {
                    var runProperties = new XElement(a + "rPr",
                        new XAttribute("lang", "en-US"),
                        new XAttribute("sz", fontSize),
                        new XAttribute("b", style.Bold ? 1 : 0),
                        new XAttribute("dirty", 0),
                        new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", style.ColorHex))),
                        new XElement(a + "latin", new XAttribute("typeface", "Cambria Math")));
                    var mathRunProperties = run.Element(m + "rPr");
                    if (mathRunProperties is not null)
                    {
                        mathRunProperties.AddAfterSelf(runProperties);
                    }
                    else
                    {
                        run.AddFirst(runProperties);
                    }
                }

                // Serialize with exactly the m:/a: prefixes, then drop the declarations themselves -- a: is declared on the
                // enclosing paragraph (BuildParagraph) and m: on the equation's own root (EquationXml); left undeclared
                // here, the writer would invent prefixes (p1, p2...).
                foreach (var attribute in math.DescendantsAndSelf().Attributes().Where(t => t.IsNamespaceDeclaration).ToList())
                {
                    attribute.Remove();
                }

                math.Add(new XAttribute(XNamespace.Xmlns + "m", MathNs), new XAttribute(XNamespace.Xmlns + "a", DrawingNs));

                var xml = math.ToString(SaveOptions.DisableFormatting);
                return NamespaceDeclarationRegex().Replace(xml, string.Empty);
            }
            catch (Exception exc) when (exc is FormatException or System.Xml.XmlException)
            {
                return null;
            }
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\sxmlns(:\w+)?=""[^""]*""")]
        private static partial System.Text.RegularExpressions.Regex NamespaceDeclarationRegex();

        /// <summary>A rough height for a text box holding <paramref name="html"/>, so shapes below it can be placed:
        /// average character width ~0.5em, 1.2 line spacing, one extra line per formula holding a fraction.</summary>
        private static long EstimateTextHeight(string? html, int fontSize, long width)
        {
            var fontPoints = fontSize / 100d;
            if (string.IsNullOrWhiteSpace(html))
            {
                return (long)(fontPoints * 1.4 * EmuPerPoint);
            }

            var body = new HtmlParser().ParseDocument($"<body>{html}</body>").Body!;
            var blocks = body.Children.Where(t => t.NodeName is "P" or "DIV").ToList();
            var texts = blocks.Count == 0 ? [body.TextContent] : blocks.Select(t => t.TextContent).ToList();
            var charsPerLine = Math.Max(10, width / (fontPoints * 0.5 * EmuPerPoint));
            var lines = texts.Sum(t => Math.Max(1, (int)Math.Ceiling(t.Trim().Length / charsPerLine)));
            lines += body.QuerySelectorAll("br").Length;
            lines += body.QuerySelectorAll("span[data-omml-b64]")
                .Count(t => Encoding.UTF8.GetString(Convert.FromBase64String(t.GetAttribute("data-omml-b64") ?? string.Empty)).Contains("<m:f>", StringComparison.Ordinal));
            return (long)((lines * fontPoints * 1.2 * EmuPerPoint) + 91440);
        }

        private static uint NextShapeId() => Interlocked.Increment(ref shapeIdCounter);

        // ---- Pictures -------------------------------------------------------------------------------------

        private static async Task<(P.Picture Picture, long Width, long Height)?> EmbedPictureFromSourceAsync(
            SlidePart slidePart, string src, Lazy<HttpClient>? httpClient, long x, long y, long maxWidth, long maxHeight, bool centerHorizontally = false)
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

            return EmbedPicture(slidePart, bytes, x, y, maxWidth, maxHeight, centerHorizontally);
        }

        /// <summary>
        /// Embeds a picture at its original resolution (never resampled -- the slide scales it) and places it at its
        /// original size (96dpi px), shrunk proportionally only if it doesn't fit <paramref name="maxWidth"/> x
        /// <paramref name="maxHeight"/> -- never enlarged or stretched out of shape. Returns the placed size.
        /// </summary>
        private static (P.Picture Picture, long Width, long Height)? EmbedPicture(
            SlidePart slidePart, byte[] bytes, long x, long y, long maxWidth, long maxHeight, bool centerHorizontally = false)
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                return null;
            }

            var width = (double)bitmap.Width * EmuPerPixel;
            var height = (double)bitmap.Height * EmuPerPixel;
            var scale = Math.Min(1d, Math.Min(maxWidth / width, maxHeight / height));
            var cx = (long)(width * scale);
            var cy = (long)(height * scale);
            if (centerHorizontally)
            {
                x += (maxWidth - cx) / 2;
            }

            var imagePart = slidePart.AddImagePart(ImagePartType.Png);
            using (var pngStream = new MemoryStream())
            {
                using (var image = SKImage.FromBitmap(bitmap))
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

            return (picture, cx, cy);
        }

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

        // ---- Minimal required parts: theme / slide master / slide layout ------------------------------

        private static ThemePart BuildThemePart(PresentationPart presentationPart)
        {
            var themePart = presentationPart.AddNewPart<ThemePart>();
            var theme = new A.Theme { Name = "GamaEdtechExamTheme" };
            var themeElements = new A.ThemeElements();

            var colorScheme = new A.ColorScheme { Name = "GamaEdtech" };
            _ = colorScheme.AppendChild(BuildThemeColor<A.Dark1Color>(new A.SystemColor { Val = A.SystemColorValues.WindowText }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Light1Color>(new A.SystemColor { Val = A.SystemColorValues.Window }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Dark2Color>(new A.RgbColorModelHex { Val = Navy }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Light2Color>(new A.RgbColorModelHex { Val = RowGrayBg }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent1Color>(new A.RgbColorModelHex { Val = NavyMid }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent2Color>(new A.RgbColorModelHex { Val = Yellow }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent3Color>(new A.RgbColorModelHex { Val = BorderGray }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent4Color>(new A.RgbColorModelHex { Val = TextMuted }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent5Color>(new A.RgbColorModelHex { Val = TextDark }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Accent6Color>(new A.RgbColorModelHex { Val = NavyBadge }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.Hyperlink>(new A.RgbColorModelHex { Val = NavyMid }));
            _ = colorScheme.AppendChild(BuildThemeColor<A.FollowedHyperlinkColor>(new A.RgbColorModelHex { Val = Navy }));
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

            // The layout's own relationship back to its master is required by the format (every slideLayout part
            // references its slideMaster). PowerPoint and LibreOffice tolerated it missing, but Google Slides rejects
            // the whole deck ("File could not open", found 2026-09-25 by importing variants: this was the one
            // difference that mattered).
            _ = slideLayoutPart.CreateRelationshipToPart(slideMasterPart);

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
