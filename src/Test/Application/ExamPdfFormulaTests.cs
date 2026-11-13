namespace GamaEdtech.Test.Application
{
    using System.Text.RegularExpressions;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Game;

    using Xunit;

    /// <summary>The PDF export makes each <c>$...$</c> formula <c>\displaystyle</c> (Word's full-size look) before MathJax.
    /// An escaped <c>\$</c> is a plain dollar sign (money), never a delimiter. Pure in-memory tests.</summary>
    public partial class ExamPdfFormulaTests
    {
        // Exam 2528 Q2: the PDF showed "$\displaystyle 800, liabilities $1120 and capital $320".
        [Theory]
        [InlineData(@"assets \$800, liabilities \$1120 and capital \$320")]
        [InlineData(@"\$12 000")]
        [InlineData(@"Recording \$90 as an expense understated profit by \$90.")]
        public async Task EscapedDollarIsNotAFormula(string option) =>
            Assert.Equal(option, await OptionAAsync(option));

        [Theory]
        [InlineData(@"$40^{\circ}$", @"$\displaystyle 40^{\circ}$")]
        [InlineData(@"$\frac{3}{4}$", @"$\displaystyle \frac{3}{4}$")]
        [InlineData(@"costs \$5 and weighs $5\ \text{kg}$", @"costs \$5 and weighs $\displaystyle 5\ \text{kg}$")]
        [InlineData(@"$a \$ b$", @"$\displaystyle a \$ b$")]
        public async Task FormulaStillBecomesDisplayStyle(string option, string expected) =>
            Assert.Equal(expected, await OptionAAsync(option));

        /// <summary>Option A's text as the PDF body holds it, tags removed.</summary>
        private static async Task<string> OptionAAsync(string option)
        {
            var exam = new ExamInformationResponseDto
            {
                Exam = new() { Title = "Formulas", TestsCount = 1 },
                Tests =
                [
                    new()
                    {
                        Question = "<p>Which one?</p>",
                        QuestionType = "fourchoice",
                        OptionA = $"<p>{option}</p>",
                        OptionB = "<p>OPTION-B</p>",
                        OptionC = "<p>OPTION-C</p>",
                        OptionD = "<p>OPTION-D</p>",
                        CorrectOption = 'A',
                    },
                ],
            };

            var html = (await ExamPdfHtmlBuilder.BuildAsync(exam, ExamExportImageLayoutTests.BuildBrandAssets(), null)).BodyHtml;
            var start = html.IndexOf("Which one?", StringComparison.Ordinal);
            var end = html.IndexOf("OPTION-B", start, StringComparison.Ordinal);
            var cell = TagRegex().Replace(html[start..end], "|");
            return cell.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(t => t.Contains('$', StringComparison.Ordinal));
        }

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex TagRegex();
    }
}
