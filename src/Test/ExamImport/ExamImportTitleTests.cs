namespace GamaEdtech.Test.ExamImport
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.ExamImport;

    using Xunit;

    public class ExamImportTitleTests
    {
        [Fact]
        public void DefaultTitleFollowsTheTemplate() =>
            Assert.Equal("Biology 5090-11 October November 2025 Online Exam",
                ExamImportRules.Title(new ExamImportDraftDto { Subject = "Biology", Component = "5090/11", SessionMonth = 11, Year = 2025 }));

        [Fact]
        public void WithoutAComponentThePaperIsUsed() =>
            Assert.Equal("Physics Paper 1 May June 2024 Online Exam",
                ExamImportRules.Title(new ExamImportDraftDto { Subject = "Physics", Paper = "Paper 1", SessionMonth = 6, Year = 2024 }));

        [Fact]
        public void MissingPartsAreLeftOut() =>
            Assert.Equal("Chemistry 2023 Online Exam", ExamImportRules.Title(new ExamImportDraftDto { Subject = "Chemistry", Year = 2023 }));

        [Fact]
        public void ATitleTheUserSetIsKept() =>
            Assert.Equal("My mock test", ExamImportRules.Title(new ExamImportDraftDto { Subject = "Biology", Title = "My mock test" }));
    }
}
