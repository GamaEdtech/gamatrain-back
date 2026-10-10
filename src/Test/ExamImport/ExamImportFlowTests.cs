namespace GamaEdtech.Test.ExamImport
{
    using System.Text.Json;

    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Presentation.Mcp;
    using GamaEdtech.Presentation.ViewModel.ExamImport;

    using Xunit;

    using static GamaEdtech.Data.Dto.ExamImport.ExamImportReportDto;

    public class ExamImportFlowTests
    {
        private static readonly ExamImportDraftDto Draft = new() { Id = 42, Title = "Mathematics 9709/12", QuestionIds = [1, 2, 3, 4, 5] };

        private static ExamImportReviewQuestionViewModel Row(string number, string status, params string[] codes) => new()
        {
            Number = number,
            Id = status == ReviewStatus ? long.Parse(number, System.Globalization.CultureInfo.InvariantCulture) : null,
            Status = status,
            Issues = [.. codes.Select(t => new ExamImportReviewQuestionViewModel.IssueViewModel { Code = t, Message = $"Issue {t}." })],
        };

        [Fact]
        public void AiAnswersAreCountedThroughTheWholeReview()
        {
            var first = ExamImportFlow.Review(Draft, [Row("1", ReviewStatus, IssueDto.AiAnswerCode), Row("2", ReviewStatus, IssueDto.AiAnswerCode), Row("3", BlockedStatus, IssueDto.MissingAnswerCode), Row("4", ReviewStatus, "duplicateOptions")], 0);
            Assert.Contains("2 answers written by the AI", first.Question, StringComparison.Ordinal);
            Assert.Contains("Q3", first.Question, StringComparison.Ordinal);
            Assert.All(first.Options!.Where(t => t.Key is "fix" or "drop" or "aiAnswers"), t => Assert.Contains("aiAnswers=2", t.Next, StringComparison.Ordinal));
            Assert.Equal(["4"], first.Remaining!.Select(t => t.Number));

            var second = ExamImportFlow.Review(Draft, first.Remaining!, 2);
            Assert.Contains("2 answers written by the AI", second.Question, StringComparison.Ordinal);
            Assert.Contains("Q4", second.Question, StringComparison.Ordinal);

            var last = ExamImportFlow.Review(Draft, second.Remaining!, 2);
            Assert.Contains("Nothing needs a decision", last.Question, StringComparison.Ordinal);
            Assert.Contains("2 answers written by the AI", last.Question, StringComparison.Ordinal);
        }

        [Fact]
        public void RemainingRowsAreSentOnce()
        {
            List<ExamImportReviewQuestionViewModel> flagged = [.. Enumerable.Range(1, 30).Select(t => Row($"{t}", t % 2 == 0 ? ReviewStatus : FailedStatus, "longText"))];
            var ask = ExamImportFlow.Review(Draft, flagged, 0);
            var json = JsonSerializer.Serialize(ask, ExamImportFlow.JsonOptions);

            Assert.Equal(29, ask.Remaining!.Count);
            Assert.Equal(2, json.Split("\"number\":\"17\"").Length); // question 17's row, once
            Assert.All(ask.Options!.Where(t => t.Key is "fix" or "drop"), t => Assert.Contains("flagged=<remaining>", t.Next, StringComparison.Ordinal));
        }

        [Fact]
        public void APickedDetailIsAskedFromItsOptions()
        {
            var ask = ExamImportFlow.Choice(new() { Pick = "board", Options = [new() { Id = 7, Title = "Cambridge" }, new() { Id = 8, Title = "Edexcel" }] })!;
            Assert.Equal("boardId", ask.Id);
            Assert.Equal(["7", "8"], ask.Options!.Select(t => t.Key));
            Assert.Contains("boardId=<key>", ask.Next, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDurationIsTyped()
        {
            var ask = ExamImportFlow.Choice(new() { Pick = "duration" })!;
            Assert.Equal("durationMinutes", ask.Id);
            Assert.Contains("durationMinutes=<value>", ask.Field!.Next, StringComparison.Ordinal);
        }

        [Fact]
        public void AnExistingDraftIsContinuedOrReplaced()
        {
            var ask = ExamImportFlow.Choice(new() { ExistingDraft = Draft })!;
            Assert.Equal("existingDraft", ask.Id);
            Assert.Equal(["continue", "replace", "back"], ask.Options!.Select(t => t.Key));
        }

        [Fact]
        public void DetailsNeedNoChoice() =>
            Assert.Null(ExamImportFlow.Choice(new() { Details = Draft }));
    }
}
