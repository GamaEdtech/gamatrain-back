namespace GamaEdtech.Test.ExamImport
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.ExamImport;

    using Xunit;

    public class ExamImportSearchTests
    {
        /// <summary>The words of gama-api's paper types (Paper 1, Paper 2, Topical).</summary>
        private static readonly HashSet<string> PaperTypeWords = new(StringComparer.Ordinal) { "PAPER", "1", "2", "TOPICAL" };

        private static readonly ExamImportPastPaperDto Paper = new() { Id = 1, Title = "Mathematics 9709/12", Classification = "Paper 1", Year = 2024, Month = 6 };

        [Fact]
        public void WordsAreUpperCaseRunsOfLettersAndDigits() =>
            Assert.Equal(["MATHEMATICS", "9709", "12", "MAY", "JUNE"], ExamImportService.Words("Mathematics 9709/12 — May/June"));

        [Theory]
        [InlineData("9709 paper 1 2024", "9709", 2024)]
        [InlineData("Mathematics 2024", "MATHEMATICS", 2024)]
        [InlineData("2058 paper 1 2023", "2058", 2023)] // a syllabus code comes before the year
        [InlineData("2058", "2058", null)] // a number alone is searched in the title
        [InlineData("9709 topical", "9709", null)] // a paper type isn't in the title
        [InlineData("paper 1", null, null)]
        [InlineData("topical 2024", "2024", null)]
        public void SearchTermsPickTheTitleWordAndTheYear(string query, string? anchor, int? year) =>
            Assert.Equal((anchor, year), ExamImportService.SearchTerms(ExamImportService.Words(query), PaperTypeWords));

        [Theory]
        [InlineData("9709 paper 1 2024")] // the year is only the paper's own
        [InlineData("math 9709 may june")] // a word as a prefix, the session in words
        [InlineData("9709/12 2024")]
        public void PaperHasEveryWord(string query) =>
            Assert.True(ExamImportService.HasWords(Paper, ExamImportService.Words(query)));

        [Theory]
        [InlineData("970 2024")] // a number matches exactly
        [InlineData("9709 paper 2")]
        [InlineData("9709 2023")]
        [InlineData("9709 october")]
        public void PaperLacksAWord(string query) =>
            Assert.False(ExamImportService.HasWords(Paper, ExamImportService.Words(query)));
    }
}
