namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;

    using Xunit;

    using OoxmlMath = DocumentFormat.OpenXml.Math;

    /// <summary>The Word export's equations are written so LibreOffice can open them. Pure in-memory tests.</summary>
    public class ExamWordEquationTests
    {
        private const string Ns = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

        [Fact]
        public void MathRunTextIsSplitBetweenAdjacentLetters()
        {
            // Exam 2327 Q32's answer: "It" in one run is LibreOffice's italic command, and crashed Writer.
            var math = new OoxmlMath.OfficeMath($"<m:oMath {Ns}><m:r><m:t xml:space=\"preserve\">Q=It=2.0(3600)=7200</m:t></m:r></m:oMath>");

            ExamWordRichText.SplitAdjacentLetters(math);

            Assert.Equal(["Q=I", "t=2.0(3600)=7200"], math.Descendants<OoxmlMath.Text>().Select(t => t.Text));
        }

        [Fact]
        public void NormalTextAndSingleLetterRunsAreLeftAsTheyAre()
        {
            var math = new OoxmlMath.OfficeMath($"<m:oMath {Ns}><m:r><m:rPr><m:nor /></m:rPr><w:rPr /><m:t>it is kg</m:t></m:r><m:r><m:t>x=2</m:t></m:r></m:oMath>");

            ExamWordRichText.SplitAdjacentLetters(math);

            Assert.Equal(["it is kg", "x=2"], math.Descendants<OoxmlMath.Text>().Select(t => t.Text));
        }
    }
}
