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

        [Theory]
        [InlineData("press ^ to continue")]
        [InlineData("the ^ symbol")]
        [InlineData("a^b")]
        public void CaretThatIsNotAPowerStaysLiteral(string text) =>
            Assert.Equal($"<p>{text}</p>", ExamImportText.ToHtml(text));
    }
}
