namespace GamaEdtech.Test.ExamImport
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.ExamImport;

    using Xunit;

    /// <summary>
    /// The review flag for a choice question saved with the letters as its options and the choices in its figure (exam
    /// 2517 Q18: four separate graphs sharing one key, cut as one picture instead of four option figures).
    /// </summary>
    public class ExamImportOptionCheckTests
    {
        private static ExamImportQuestionDto Question(IReadOnlyList<string>? options, string? figure = "1-a/figure.png", IReadOnlyList<string>? optionFigures = null) =>
            ExamImportRules.Normalize(new()
            {
                Number = "18",
                Type = "fourchoice",
                Text = "Which graph is correct?",
                Options = options,
                Correct = "A",
                Answer = "Graph A.",
                NeedsFigure = figure is not null,
                Figure = figure,
                OptionFigures = optionFigures,
            });

        private static bool Flagged(ExamImportQuestionDto question) =>
            ExamImportRules.Check(question, []).Any(t => t.Code == ExamImportRules.LettersOnlyOptionsCode);

        [Theory]
        [InlineData("A", "B", "C", "D")]
        [InlineData("a", "b", "c", "d")]
        [InlineData("(A)", "(B)", "(C)", "(D)")]
        [InlineData(" A. ", "B.", "C.", "D.")]
        public void LetterOptionsWithAFigureAreFlagged(string a, string b, string c, string d) =>
            Assert.True(Flagged(Question([a, b, c, d])));

        [Fact]
        public void TheFlagIsAReviewNotABlock()
        {
            var question = Question(["A", "B", "C", "D"]);
            Assert.Equal(ExamImportReportDto.ReviewStatus, ExamImportRules.StatusOf(question, ExamImportRules.Check(question, [])));
        }

        [Fact]
        public void TableRowOptionsAreNotFlagged() => Assert.False(Flagged(Question(["Row A", "Row B", "Row C", "Row D"])));

        [Fact]
        public void TextOptionsAreNotFlagged() => Assert.False(Flagged(Question(["$2520", "$3000", "$12 000", "$12 480"])));

        [Fact]
        public void LettersWithoutAFigureAreNotFlagged() => Assert.False(Flagged(Question(["A", "B", "C", "D"], figure: null)));

        [Fact]
        public void LettersOutOfOrderAreNotFlagged() => Assert.False(Flagged(Question(["B", "A", "C", "D"])));

        [Fact]
        public void OptionFiguresAreNotFlagged() =>
            Assert.False(Flagged(Question(null, optionFigures: ["1-a/a.png", "1-a/b.png", "1-a/c.png", "1-a/d.png"])));
    }
}
