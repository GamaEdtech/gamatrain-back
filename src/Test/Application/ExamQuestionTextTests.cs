namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;

    using Xunit;

    public class ExamQuestionTextTests
    {
        [Fact]
        public void TheExcerptKeepsTheMathForMathJax()
            => Assert.Equal("Balance $Ca_3(PO_4)_2$ & \\(x^2\\)", ExamQuestionText.Excerpt("<p>Balance $Ca_3(PO_4)_2$ &amp;</p><p>\\(x^2\\)</p>", 200));

        [Theory]
        [InlineData("Ratio of $CaSiO_3$ to $H_3PO_4$", 22, "Ratio of $CaSiO_3$ to…")]
        [InlineData("Ratio of $CaSiO_3$ to $H_3PO_4$", 26, "Ratio of $CaSiO_3$ to…")]
        [InlineData("Find \\(x^2+y^2\\) now", 10, "Find…")]
        [InlineData("Costs \\$5 and more", 8, "Costs \\$…")]
        public void TheExcerptNeverCutsAFormulaOpen(string text, int length, string expected)
            => Assert.Equal(expected, ExamQuestionText.Excerpt(text, length));
    }
}
