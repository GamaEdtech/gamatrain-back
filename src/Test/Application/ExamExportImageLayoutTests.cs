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

    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using W = GamaEdtech.Application.Service.ExamWordDocumentBuilder;
    using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

    /// <summary>How the Word and Pdf exports size a question's own image and all-image options. Pure in-memory
    /// tests - no host, database or Redis.</summary>
    public class ExamExportImageLayoutTests
    {
        private const int QuestionImageWidthPx = 400;
        private const int SecondQuestionImageWidthPx = 300;
        private const int OptionImageWidthPx = 600;

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
                Assert.Contains($"colspan=\"{W.ContentFineColumnCount}\"><img class=\"rich\" src=\"{test.QuestionFile}\"", page.BodyHtml, StringComparison.Ordinal);
            }

            var cap = $"max-width:{W.MaxImageOptionWidthPx.ToString(CultureInfo.InvariantCulture)}px";
            Assert.Equal(4, page.BodyHtml.Split(cap).Length - 1);
        }

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

        private static HeaderBrandAssets BuildBrandAssets()
        {
            var png = PngBytes(8, 8);
            var icon = new HeaderIcon(png, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
            var levelIcons = new Dictionary<string, HeaderIcon> { ["Easy"] = icon, ["Medium"] = icon, ["Hard"] = icon };
            return new(png, icon.Svg, png, png, png, icon.Svg, levelIcons, icon, icon, icon);
        }

        private static string Png(int width, int height) => "data:image/png;base64," + Convert.ToBase64String(PngBytes(width, height));

        /// <summary>A plain white 8-bit grayscale PNG of the given size.</summary>
        private static byte[] PngBytes(int width, int height)
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
