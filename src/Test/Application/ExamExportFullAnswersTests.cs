namespace GamaEdtech.Test.Application
{
    using System.Text;

    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Game;

    using Xunit;

    using A = DocumentFormat.OpenXml.Drawing;
    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using W = GamaEdtech.Application.Service.ExamWordDocumentBuilder;

    /// <summary>The exports' "Full Answers": every question's full answer, multiple-choice ones included, after the
    /// Answer Key and always from a new page (PowerPoint: "FULL ANSWER" slides). Pure in-memory tests.</summary>
    public class ExamExportFullAnswersTests
    {
        private const string ChoiceAnswer = "Weight is a vector because it has a direction.";
        private const string DescriptiveAnswer = "Work done equals force times distance moved.";

        [Fact]
        public async Task WordPutsEveryFullAnswerOnANewPageAfterTheAnswerKey()
        {
            var bytes = await W.BuildAsync(BuildExam(), ExamExportImageLayoutTests.BuildBrandAssets(), null, new Lazy<HttpClient>(() => new HttpClient()), googleDocsCompatible: false);

            using var stream = new MemoryStream(bytes);
            using var document = WordprocessingDocument.Open(stream, false);

            // The body as one string, "|" for each page break, in document order.
            var flow = new StringBuilder();
            foreach (var element in document.MainDocumentPart!.Document!.Body!.Descendants())
            {
                if (element is Ooxml.Break { Type.Value: var type } && type == Ooxml.BreakValues.Page)
                {
                    _ = flow.Append('|');
                }
                else if (element is Ooxml.Text run)
                {
                    _ = flow.Append(run.Text).Append(' ');
                }
            }

            var text = flow.ToString();
            var keyAt = text.IndexOf("Answer Key", StringComparison.Ordinal);
            var fullAt = text.IndexOf("Full Answers", StringComparison.Ordinal);
            Assert.True(keyAt >= 0 && fullAt > keyAt);
            Assert.Contains('|', text[keyAt..fullAt]);
            Assert.True(text.IndexOf(ChoiceAnswer, StringComparison.Ordinal) > fullAt);
            Assert.True(text.IndexOf(DescriptiveAnswer, StringComparison.Ordinal) > fullAt);
            Assert.DoesNotContain("Descriptive Answers", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PdfPutsEveryFullAnswerInASectionStartingOnANewPage()
        {
            var page = await ExamPdfHtmlBuilder.BuildAsync(BuildExam(), ExamExportImageLayoutTests.BuildBrandAssets(), null);
            var html = page.BodyHtml;

            // Both sections open with the class that breaks the page before it.
            Assert.Contains(".answer-key{break-before:page;", html, StringComparison.Ordinal);
            var keyAt = html.IndexOf("class=\"ak-title\">Answer Key<", StringComparison.Ordinal);
            var fullAt = html.IndexOf("class=\"ak-title\">Full Answers<", StringComparison.Ordinal);
            Assert.True(keyAt >= 0 && fullAt > keyAt);
            Assert.EndsWith("<div class=\"answer-key\"><div style=\"height:3.3pt\"></div>", html[..html.IndexOf("<div class=\"ak-title\">Full Answers<", StringComparison.Ordinal)], StringComparison.Ordinal);
            Assert.True(html.IndexOf(ChoiceAnswer, StringComparison.Ordinal) > fullAt);
            Assert.True(html.IndexOf(DescriptiveAnswer, StringComparison.Ordinal) > fullAt);
        }

        [Fact]
        public async Task PowerPointShowsAMultipleChoiceFullAnswerOnItsOwnSlide()
        {
            var png = ExamExportImageLayoutTests.PngBytes(8, 8);
            var bytes = await ExamPresentationBuilder.BuildAsync(BuildExam(), new(png, png, png, png, null), new Lazy<HttpClient>(() => new HttpClient()));

            using var stream = new MemoryStream(bytes);
            using var deck = PresentationDocument.Open(stream, false);
            var slides = deck.PresentationPart!.SlideParts.Select(t => string.Join(" ", t.Slide!.Descendants<A.Text>().Select(text => text.Text))).ToList();

            // Q1: the highlighted-option slide leads on to the full answer, which has a slide of its own.
            Assert.Contains(slides, t => t.Contains("ANSWER", StringComparison.Ordinal) && t.Contains("Full Answer", StringComparison.Ordinal));
            Assert.Contains(slides, t => t.Contains("FULL ANSWER", StringComparison.Ordinal) && t.Contains(ChoiceAnswer, StringComparison.Ordinal));
            Assert.Contains(slides, t => t.Contains("FULL ANSWER", StringComparison.Ordinal) && t.Contains(DescriptiveAnswer, StringComparison.Ordinal));
        }

        private static ExamInformationResponseDto BuildExam() => new()
        {
            Exam = new() { Title = "Full answers", TestsCount = 2 },
            Tests =
            [
                new()
                {
                    Question = "<p>Which quantity is a vector?</p>",
                    QuestionType = "fourchoice",
                    OptionA = "density",
                    OptionB = "mass",
                    OptionC = "pressure",
                    OptionD = "weight",
                    CorrectOption = 'D',
                    AnswerHtml = $"<p>{ChoiceAnswer}</p>",
                },
                new()
                {
                    Question = "<p>Define work done.</p>",
                    QuestionType = "descriptive",
                    AnswerHtml = $"<p>{DescriptiveAnswer}</p>",
                },
            ],
        };
    }
}
