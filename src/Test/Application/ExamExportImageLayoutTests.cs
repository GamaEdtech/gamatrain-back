namespace GamaEdtech.Test.Application
{
    using System.Buffers.Binary;
    using System.Globalization;
    using System.IO.Compression;
    using System.Text;

    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Game;

    using Xunit;

    using A = DocumentFormat.OpenXml.Drawing;
    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using P = DocumentFormat.OpenXml.Presentation;
    using W = GamaEdtech.Application.Service.ExamWordDocumentBuilder;
    using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

    /// <summary>How the Word, Pdf and PowerPoint exports size a question's own image and all-image options. Pure
    /// in-memory tests - no host, database or Redis.</summary>
    public class ExamExportImageLayoutTests
    {
        private const int QuestionImageWidthPx = 400;
        private const int SecondQuestionImageWidthPx = 300;
        private const int OptionImageWidthPx = 600;
        private const int TallImageWidthPx = 300;
        private const int TallImageHeightPx = 700;

        [Fact]
        public async Task WordShowsQuestionImagesAtTheirOwnSizeAndImageOptionsInAReadableGrid()
        {
            var bytes = await W.BuildAsync(BuildExam(), BuildBrandAssets(), null, new Lazy<HttpClient>(() => new HttpClient()), googleDocsCompatible: false);

            using var stream = new MemoryStream(bytes);
            using var document = WordprocessingDocument.Open(stream, false);
            var body = document.MainDocumentPart!.Document!.Body!;
            var widthsPx = body.Descendants<Wp.Extent>().Select(t => (int)(t.Cx!.Value / W.EmuPerPixel)).ToList();

            // Both questions keep their own image at its original width: not the old 150px side column, and not
            // dropped because the options are images.
            Assert.Contains(QuestionImageWidthPx, widthsPx);
            Assert.Contains(SecondQuestionImageWidthPx, widthsPx);

            // The four image options are shrunk to their 2x2 cell, well above the old ~108px.
            Assert.Equal(4, widthsPx.Count(t => t == W.MaxImageOptionWidthPx));
            Assert.True(W.MaxImageOptionWidthPx > 250);

            Assert.Empty(body.Descendants<Ooxml.VerticalMerge>());
        }

        [Fact]
        public async Task PdfShowsQuestionImagesInTheirOwnRowAndImageOptionsInAReadableGrid()
        {
            var exam = BuildExam();
            var page = await ExamPdfHtmlBuilder.BuildAsync(exam, BuildBrandAssets(), null);

            Assert.DoesNotContain("rowspan", page.BodyHtml, StringComparison.Ordinal);
            foreach (var test in exam.Tests!)
            {
                Assert.Contains($"colspan=\"{W.ContentFineColumnCount}\"><img class=\"rich qimg\" src=\"{test.QuestionFile}\"", page.BodyHtml, StringComparison.Ordinal);
            }

            var cap = $"max-width:{W.MaxImageOptionWidthPx.ToString(CultureInfo.InvariantCulture)}px;max-height:{W.MaxImageOptionHeightPx.ToString(CultureInfo.InvariantCulture)}px;";
            Assert.Equal(4, page.BodyHtml.Split(cap).Length - 1);
        }

        [Fact]
        public async Task WordCapsATallQuestionImageInHeightAndKeepsItsAspectRatio()
        {
            var bytes = await W.BuildAsync(BuildTallDiagramExam(), BuildBrandAssets(), null, new Lazy<HttpClient>(() => new HttpClient()), googleDocsCompatible: false);

            using var stream = new MemoryStream(bytes);
            using var document = WordprocessingDocument.Open(stream, false);
            var diagram = document.MainDocumentPart!.Document!.Body!.Descendants<Wp.Extent>().Single(t => t.Cy!.Value > t.Cx!.Value);

            // 300x700 is shrunk to the height cap, both sides by the same factor (exam 2327 Q14 filled most of a page).
            Assert.Equal(W.MaxQuestionImageHeightPx, (int)(diagram.Cy!.Value / W.EmuPerPixel));
            Assert.Equal(TallImageWidthPx / (double)TallImageHeightPx, diagram.Cx!.Value / (double)diagram.Cy.Value, 2);
        }

        [Fact]
        public async Task PdfCapsAQuestionImageInHeightKeepingItsAspectRatio()
        {
            var page = await ExamPdfHtmlBuilder.BuildAsync(BuildTallDiagramExam(), BuildBrandAssets(), null);

            // max-height with width/height auto: the browser shrinks both sides together.
            Assert.Contains($"img.qimg{{max-height:{W.MaxQuestionImageHeightPx.ToString(CultureInfo.InvariantCulture)}px;width:auto;height:auto;}}", page.BodyHtml, StringComparison.Ordinal);
            Assert.Contains("<img class=\"rich qimg\"", page.BodyHtml, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PowerPointKeepsALongQuestionWithATallDiagramOnOneSlideWithoutStretchingIt()
        {
            var assets = new ExamPresentationBuilder.DeckAssets(PngBytes(8, 8), PngBytes(8, 8), PngBytes(8, 8), PngBytes(8, 8), null);
            var bytes = await ExamPresentationBuilder.BuildAsync(BuildTallDiagramExam(), assets, new Lazy<HttpClient>(() => new HttpClient()));

            using var stream = new MemoryStream(bytes);
            using var deck = PresentationDocument.Open(stream, false);
            var slides = deck.PresentationPart!.SlideParts.Select(t => t.Slide!).ToList();

            // Used to be the text alone on one slide, then a sliver of the diagram and the options on a "CONTINUED" one.
            Assert.DoesNotContain(slides, t => t.Descendants<A.Text>().Any(text => text.Text == "CONTINUED"));

            var diagrams = slides.SelectMany(t => t.Descendants<P.Picture>())
                .Select(t => t.ShapeProperties!.GetFirstChild<A.Transform2D>()!.Extents!)
                .Where(t => t.Cy!.Value > 2 * t.Cx!.Value)
                .ToList();
            Assert.NotEmpty(diagrams);
            Assert.All(diagrams, t =>
            {
                Assert.Equal(TallImageWidthPx / (double)TallImageHeightPx, t.Cx!.Value / (double)t.Cy!.Value, 2);
                Assert.True(t.Cy.Value > 6858000 / 2, "the diagram should take most of the slide's height");
            });
        }

        [Fact]
        public async Task PowerPointKeepsADiagramAndFourImageOptionsOnOneSlide()
        {
            var assets = new ExamPresentationBuilder.DeckAssets(PngBytes(8, 8), PngBytes(8, 8), PngBytes(8, 8), PngBytes(8, 8), null);
            var exam = new ExamInformationResponseDto
            {
                Exam = new() { Title = "Image options", TestsCount = 1 },
                Tests =
                [
                    new()
                    {
                        // Exam 2327 Q40's shape: the diagram used to be dropped, then pushed onto a continuation slide.
                        Question = "<p>A nucleus X is plotted on a graph of neutron number N against proton number Z.</p><p>The nucleus undergoes alpha-decay.</p><p>Which arrow shows the changes in N and Z?</p>",
                        QuestionFile = Png(330, 280),
                        QuestionType = "fourchoice",
                        TestImageAnswers = true,
                        OptionAFile = Png(330, 280),
                        OptionBFile = Png(330, 280),
                        OptionCFile = Png(330, 280),
                        OptionDFile = Png(330, 280),
                        CorrectOption = 'C',
                    },
                ],
            };

            var bytes = await ExamPresentationBuilder.BuildAsync(exam, assets, new Lazy<HttpClient>(() => new HttpClient()));

            using var stream = new MemoryStream(bytes);
            using var deck = PresentationDocument.Open(stream, false);
            var slides = deck.PresentationPart!.SlideParts.Select(t => t.Slide!).ToList();
            Assert.DoesNotContain(slides, t => t.Descendants<A.Text>().Any(text => text.Text == "CONTINUED"));

            // The question slide shows the diagram and the four options, none of them stretched.
            var questionSlide = slides.Single(t => t.Show?.Value != false && t.Descendants<A.Text>().Any(text => text.Text.Contains("Show Answer", StringComparison.Ordinal)));
            var pictures = questionSlide.Descendants<P.Picture>().Select(t => t.ShapeProperties!.GetFirstChild<A.Transform2D>()!.Extents!).Where(t => t.Cx!.Value > 200000).ToList();
            Assert.Equal(5, pictures.Count);
            Assert.All(pictures, t => Assert.Equal(330 / 280d, t.Cx!.Value / (double)t.Cy!.Value, 2));
        }

        /// <summary>Exam 2327 Q14's shape: three paragraphs of question, a tall portrait diagram, four short options.</summary>
        private static ExamInformationResponseDto BuildTallDiagramExam() => new()
        {
            Exam = new() { Title = "Tall diagram", TestsCount = 1 },
            Tests =
            [
                new()
                {
                    Question = "<p>A U-tube has a left arm of cross-sectional area A and a right arm of cross-sectional area 4A. The tube contains water of density 1000 kg m-3 and oil of density 850 kg m-3 as shown.</p>"
                        + "<p>The column of oil on top of the water in the left-hand arm is of length 30.0 cm.</p>"
                        + "<p>What is the difference in height x between the levels in the two arms of the tube?</p>",
                    QuestionFile = Png(TallImageWidthPx, TallImageHeightPx),
                    QuestionType = "fourchoice",
                    OptionA = "4.5 cm",
                    OptionB = "6.2 cm",
                    OptionC = "23.8 cm",
                    OptionD = "25.5 cm",
                    CorrectOption = 'A',
                },
            ],
        };

        private static ExamInformationResponseDto BuildExam() => new()
        {
            Exam = new() { Title = "Image layout", TestsCount = 2 },
            Tests =
            [
                new()
                {
                    Question = "<p>Which quantities could P and Q represent?</p>",
                    QuestionFile = Png(QuestionImageWidthPx, 200),
                    QuestionType = "fourchoice",
                    AnswerViewType = "2",
                    OptionA = "P: speed, Q: acceleration",
                    OptionB = "P: acceleration, Q: speed",
                    OptionC = "P: distance fallen, Q: speed",
                    OptionD = "P: speed, Q: distance fallen",
                },
                new()
                {
                    Question = "<p>What are the colours of the fringes seen on the screen?</p>",
                    QuestionFile = Png(SecondQuestionImageWidthPx, 100),
                    QuestionType = "fourchoice",
                    TestImageAnswers = true,
                    OptionAFile = Png(OptionImageWidthPx, 150),
                    OptionBFile = Png(OptionImageWidthPx, 150),
                    OptionCFile = Png(OptionImageWidthPx, 150),
                    OptionDFile = Png(OptionImageWidthPx, 150),
                },
            ],
        };

        internal static HeaderBrandAssets BuildBrandAssets()
        {
            var png = PngBytes(8, 8);
            var icon = new HeaderIcon(png, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
            var levelIcons = new Dictionary<string, HeaderIcon> { ["Easy"] = icon, ["Medium"] = icon, ["Hard"] = icon };
            return new(png, icon.Svg, png, png, png, icon.Svg, levelIcons, icon, icon, icon);
        }

        internal static string Png(int width, int height) => "data:image/png;base64," + Convert.ToBase64String(PngBytes(width, height));

        /// <summary>A plain white 8-bit grayscale PNG of the given size.</summary>
        internal static byte[] PngBytes(int width, int height)
        {
            using var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8; // bit depth; color type 0 (grayscale), default compression/filter, no interlace
            WriteChunk(png, "IHDR", header);

            using var pixels = new MemoryStream();
            using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[width + 1]; // filter byte 0 + one white byte per pixel
                Array.Fill(row, (byte)0xFF, 1, width);
                for (var y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            WriteChunk(png, "IDAT", pixels.ToArray());
            WriteChunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void WriteChunk(MemoryStream png, string type, byte[] data)
        {
            var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var crc = 0xFFFFFFFFu;
            foreach (var b in typeAndData)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            png.Write(number);
            png.Write(typeAndData);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            png.Write(number);
        }
    }
}
