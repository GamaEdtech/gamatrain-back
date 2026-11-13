namespace GamaEdtech.Test.ExamImport
{
    using GamaEdtech.Application.Service;

    using Xunit;

    public class ExamImportTextTests
    {
        [Theory]
        [InlineData("4.2 m s^-1", @"<p>4.2 m s\(^{-1}\)</p>")]
        [InlineData("1.5 × 10^12 Hz", @"<p>1.5 × 10\(^{12}\) Hz</p>")]
        [InlineData("x^(2) + y^{-3}", @"<p>x\(^{2}\) + y\(^{-3}\)</p>")]
        [InlineData("kg m^−2", @"<p>kg m\(^{-2}\)</p>")]
        [InlineData("(a+b)^2", @"<p>(a+b)\(^{2}\)</p>")]
        public void CaretPowerOutsideMathBecomesInlineTex(string text, string expected) =>
            Assert.Equal(expected, ExamImportText.ToHtml(text));

        [Theory]
        [InlineData(@"$1.5 \times 10^{-3}\ \text{m s}^{-1}$")]
        [InlineData(@"\(x^2\) and $$y^{3}$$")]
        public void CaretInsideMathIsUntouched(string text) =>
            Assert.Equal($"<p>{text}</p>", ExamImportText.ToHtml(text));

        // Exam 2528 Q14: the two amounts were one formula, "900. This was ... for " in italics.
        [Theory]
        [InlineData(
            "On the last day of the financial year, Khalid purchased office fittings for $900. This was incorrectly recorded as office expenses for $90.",
            @"<p>On the last day of the financial year, Khalid purchased office fittings for \$900. This was incorrectly recorded as office expenses for \$90.</p>")]
        [InlineData(
            "Recording $90 as an office expense understated profit by $90. Therefore the correct answer is **C**.",
            @"<p>Recording \$90 as an office expense understated profit by \$90. Therefore the correct answer is <b>C</b>.</p>")]
        // Exam 2528 Q11 and Q27: amounts with a minus sign between them, and with a space as the thousands separator.
        [InlineData("$312 − $47 = $265.", @"<p>\$312 − \$47 = \$265.</p>")]
        [InlineData(
            "1. Depreciation of $24 000 had been included.\n2. Closing work in progress of $17 400 had been recorded as $14 700.",
            @"<p>1. Depreciation of \$24 000 had been included.<br>2. Closing work in progress of \$17 400 had been recorded as \$14 700.</p>")]
        [InlineData("Thus $360 100 − $2700 = $357 400, answer **C**.", @"<p>Thus \$360 100 − \$2700 = \$357 400, answer <b>C</b>.</p>")]
        [InlineData("overstated by $810", @"<p>overstated by \$810</p>")]
        [InlineData("Sales $12,500 and purchases $8,250.75 (net $4,249.25)", @"<p>Sales \$12,500 and purchases \$8,250.75 (net \$4,249.25)</p>")]
        [InlineData("$1,000 was paid", @"<p>\$1,000 was paid</p>")]
        [InlineData("already escaped \\$50 and $60", @"<p>already escaped \$50 and \$60</p>")]
        public void MoneyDollarIsEscaped(string text, string expected) =>
            Assert.Equal(expected, ExamImportText.ToHtml(text));

        [Theory]
        [InlineData(@"$1.5 \times 10^{-3}\ \text{m s}^{-1}$")]
        [InlineData(@"$4.2\ \text{m s}^{-1}$")]
        [InlineData("$10^{12}$")]
        [InlineData("$5$")]
        [InlineData("$2 x + 3$")]
        [InlineData("between $3$ and $4$")]
        [InlineData(@"$$\frac{3x^2-1}{x+2}$$ costs nothing")]
        [InlineData(@"\(5 + 2\) and \[7\]")]
        public void FormulaStartingWithADigitIsUntouched(string text) =>
            Assert.Equal($"<p>{text}</p>", ExamImportText.ToHtml(text));

        [Fact]
        public void MoneyNextToAFormulaKeepsTheFormula() =>
            Assert.Equal(
                @"<p>A force of $5\ \text{N}$ costs \$20 to test.</p>",
                ExamImportText.ToHtml(@"A force of $5\ \text{N}$ costs $20 to test."));

        // Exam 2527 Q21: MathJax has no \degree, so "$40\degree$" was not drawn as 40°.
        [Theory]
        [InlineData(@"$40\degree$", @"<p>$40^{\circ}$</p>")]
        [InlineData(@"$90\degree-50\degree=40\degree$", @"<p>$90^{\circ}-50^{\circ}=40^{\circ}$</p>")]
        [InlineData(@"$25\textdegree\text{C}$", @"<p>$25^{\circ}\text{C}$</p>")]
        [InlineData(@"$30^\degree$ and $30^{\degree}$", @"<p>$30^{\circ}$ and $30^{\circ}$</p>")]
        [InlineData(@"\(60\degree{}\)", @"<p>\(60^{\circ}\)</p>")]
        public void DegreeCommandBecomesCirc(string text, string expected) =>
            Assert.Equal(expected, ExamImportText.ToHtml(text));

        [Theory]
        [InlineData(@"the \degree key")]
        [InlineData(@"$\degrees$")]
        public void DegreeOutsideMathOrInAnotherWordIsUntouched(string text) =>
            Assert.Equal($"<p>{text}</p>", ExamImportText.ToHtml(text));

        [Theory]
        [InlineData("press ^ to continue")]
        [InlineData("the ^ symbol")]
        [InlineData("a^b")]
        public void CaretThatIsNotAPowerStaysLiteral(string text) =>
            Assert.Equal($"<p>{text}</p>", ExamImportText.ToHtml(text));
    }
}
