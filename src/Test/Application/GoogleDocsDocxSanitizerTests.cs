namespace GamaEdtech.Test.Application
{
    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;
    using DocumentFormat.OpenXml.Validation;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Game;

    using Xunit;

    using A = DocumentFormat.OpenXml.Drawing;
    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
    using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

    /// <summary>Pure in-memory tests - no host, database or Redis, unlike the other classes in this project.</summary>
    public class GoogleDocsDocxSanitizerTests
    {
#pragma warning disable S1075 // spec-mandated OOXML namespace URI
        private const string ShapeUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
#pragma warning restore S1075

        [Fact]
        public void ShapesAreRemovedAndEverythingElseKept()
        {
            var result = GoogleDocsDocxSanitizer.StripUnsupportedShapes(BuildDocument(withShapes: true));

            using var stream = new MemoryStream(result);
            using var document = WordprocessingDocument.Open(stream, false);
            var main = document.MainDocumentPart!;
            var header = main.HeaderParts.Single().Header!;

            Assert.Empty(header.Descendants<Wps.WordprocessingShape>());
            Assert.Empty(header.Descendants<AlternateContent>());
            Assert.DoesNotContain("wordprocessingShape", header.OuterXml, StringComparison.Ordinal);
            Assert.Empty(main.Document!.Descendants<Wps.WordprocessingShape>());

            // Kept: body text, the header table (and its text), and the floating picture.
            Assert.Contains("Hello body", main.Document!.InnerText, StringComparison.Ordinal);
            _ = Assert.Single(header.Descendants<Ooxml.Table>());
            Assert.Contains("Header cell", header.InnerText, StringComparison.Ordinal);
            _ = Assert.Single(header.Descendants<Wp.Anchor>());

            // The now-empty carrier paragraph is gone; the table is the header's first element.
            _ = Assert.IsType<Ooxml.Table>(header.FirstChild);

            Assert.Empty(new OpenXmlValidator().Validate(document));
        }

        [Fact]
        public void DocumentWithoutShapesIsReturnedUnchanged()
        {
            var original = BuildDocument(withShapes: false);

            var result = GoogleDocsDocxSanitizer.StripUnsupportedShapes(original);

            Assert.Same(original, result);
        }

        [Fact]
        public void GoogleDocsModeIsOffByDefault()
        {
            var request = new ExportExamRequestDto { UserId = 1, Url = null, SecretKey = null, ExamId = 1, FileType = null! };

            Assert.False(request.GoogleDocsCompatible);
        }

        private static byte[] BuildDocument(bool withShapes)
        {
            using var stream = new MemoryStream();
            using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                var body = With(new Ooxml.Body(), With(new Ooxml.Paragraph(), With(new Ooxml.Run(), new Ooxml.Text("Hello body"))));
                main.Document = With(new Ooxml.Document(), body);

                var headerPart = main.AddNewPart<HeaderPart>();
                var header = new Ooxml.Header();
                if (withShapes)
                {
                    var run = With(new Ooxml.Run(), WrapInAlternateContent(BuildShapeDrawing(1)), BuildShapeDrawing(2));
                    _ = header.AppendChild(With(new Ooxml.Paragraph(), run));
                }

                _ = header.AppendChild(BuildTableWithFloatingPicture(headerPart));
                headerPart.Header = header;
                headerPart.Header.Save();

                _ = body.AppendChild(With(new Ooxml.SectionProperties(), new Ooxml.HeaderReference { Type = Ooxml.HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) }));
                main.Document.Save();
            }

            return stream.ToArray();
        }

        private static T With<T>(T parent, params OpenXmlElement[] children)
            where T : OpenXmlElement
        {
            foreach (var child in children)
            {
                _ = parent.AppendChild(child);
            }

            return parent;
        }

        private static Ooxml.Drawing BuildShapeDrawing(uint id)
        {
            var shape = With(
                new Wps.WordprocessingShape(),
                new Wps.NonVisualDrawingProperties { Id = id, Name = $"shape{id}" },
                new Wps.NonVisualDrawingShapeProperties(),
                new Wps.ShapeProperties(),
                new Wps.TextBodyProperties());
            var graphic = With(new A.Graphic(), With(new A.GraphicData { Uri = ShapeUri }, shape));
            return With(new Ooxml.Drawing(), BuildAnchor(id, graphic));
        }

        private static AlternateContent WrapInAlternateContent(Ooxml.Drawing drawing) =>
            With(
                new AlternateContent(),
                With(new AlternateContentChoice { Requires = "wps" }, drawing),
                new AlternateContentFallback());

        private static Wp.Anchor BuildAnchor(uint id, A.Graphic graphic) =>
            With(
                new Wp.Anchor { SimplePos = false, RelativeHeight = id, BehindDoc = true, Locked = false, LayoutInCell = true, AllowOverlap = true },
                new Wp.SimplePosition { X = 0, Y = 0 },
                With(new Wp.HorizontalPosition { RelativeFrom = Wp.HorizontalRelativePositionValues.Page }, new Wp.PositionOffset("0")),
                With(new Wp.VerticalPosition { RelativeFrom = Wp.VerticalRelativePositionValues.Page }, new Wp.PositionOffset("0")),
                new Wp.Extent { Cx = 100000, Cy = 100000 },
                new Wp.WrapNone(),
                new Wp.DocProperties { Id = id, Name = $"drawing{id}" },
                graphic);

        private static Ooxml.Table BuildTableWithFloatingPicture(HeaderPart headerPart)
        {
            var image = headerPart.AddImagePart(ImagePartType.Png);
            using (var imageStream = new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")))
            {
                image.FeedData(imageStream);
            }

            var graphic = With(new A.Graphic(), With(new A.GraphicData { Uri = PictureUri }, new A.Blip { Embed = headerPart.GetIdOfPart(image) }));
            var pictureRun = With(new Ooxml.Run(), With(new Ooxml.Drawing(), BuildAnchor(9, graphic)));
            var cell = With(new Ooxml.TableCell(), With(new Ooxml.Paragraph(), With(new Ooxml.Run(), new Ooxml.Text("Header cell")), pictureRun));
            return With(
                new Ooxml.Table(),
                new Ooxml.TableProperties(),
                With(new Ooxml.TableGrid(), new Ooxml.GridColumn { Width = "5000" }),
                With(new Ooxml.TableRow(), cell));
        }
    }
}
