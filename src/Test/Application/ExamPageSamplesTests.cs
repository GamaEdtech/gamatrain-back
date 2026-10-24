namespace GamaEdtech.Test.Application
{
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.Provider.Core;

    using Xunit;

    /// <summary>The public exam page's sample questions (<c>GET exams/{id}</c>): never an answer, always in exam order.</summary>
    public class ExamPageSamplesTests
    {
        private static CoreExamTestResponse Question(string id, string text = "<p>What is 2 + 2?</p>") => new()
        {
            Id = id,
            Code = " fyshzk ",
            Question = text,
            OptionA = "<p>3</p>",
            OptionB = "<p>4</p>",
            OptionC = "<p>5</p>",
            OptionD = "",
            TrueAnswer = "2",
            AnswerFull = "<p>Because 2 + 2 = 4</p>",
        };

        [Fact]
        public void MapNeverCopiesTheAnswer()
        {
            var sample = ExamPageSamples.Map(Question("29913"));

            Assert.NotNull(sample);
            var json = JsonSerializer.Serialize(sample);
            Assert.DoesNotContain("Because", json, System.StringComparison.Ordinal);
            Assert.DoesNotContain("TrueAnswer", json, System.StringComparison.Ordinal);
            Assert.Equal(29913, sample.Id);
            Assert.Equal("fyshzk", sample.Code);
            Assert.Equal(["<p>3</p>", "<p>4</p>", "<p>5</p>"], sample.Options.Select(o => o.Text));
            Assert.Equal([1, 2, 3], sample.Options.Select(o => o.Key));
        }

        [Fact]
        public void OptionKeysKeepTheirNumberWhenAnEarlierOptionIsEmpty()
        {
            var question = Question("1");
            question.OptionB = "";

            var sample = ExamPageSamples.Map(question);

            Assert.NotNull(sample);
            Assert.Equal([1, 3], sample.Options.Select(o => o.Key));
        }

        [Fact]
        public void MapSanitizesTheHtml()
        {
            var sample = ExamPageSamples.Map(Question("1", "<p>Hi</p><script>alert(1)</script><img src=x onerror=alert(1)>"));

            Assert.NotNull(sample);
            Assert.DoesNotContain("script", sample.Question, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onerror", sample.Question, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MapSkipsLockedEmptyOrUnidentifiedQuestions()
        {
            var locked = Question("1");
            locked.Paywall = new() { Locked = true };

            Assert.Null(ExamPageSamples.Map(locked));
            Assert.Null(ExamPageSamples.Map(Question("2", "")));
            Assert.Null(ExamPageSamples.Map(Question("not-a-number")));
            Assert.Null(ExamPageSamples.Map(null));
        }

        [Fact]
        public void MapKeepsAnImageOnlyQuestionButDropsNonHttpImages()
        {
            var imageOnly = Question("1", "");
            imageOnly.QuestionFile = "https://core.gamatrain.com/uploads/q.png";
            imageOnly.OptionAFile = "0";
            imageOnly.OptionBFile = "javascript:alert(1)";

            var sample = ExamPageSamples.Map(imageOnly);

            Assert.NotNull(sample);
            Assert.Equal("https://core.gamatrain.com/uploads/q.png", sample.Image);
            Assert.All(sample.Options, o => Assert.Null(o.Image));
        }

        [Fact]
        public void SelectKeepsExamOrderSkipsFailuresAndStopsAtTheCount()
        {
            CoreExamTestResponse?[] inOrder = [Question("10"), null, Question("11", ""), Question("12"), Question("13"), Question("14")];

            var samples = ExamPageSamples.Select(inOrder, 3);

            Assert.Equal([10L, 12L, 13L], samples.Select(s => s.Id));
        }

        [Fact]
        public void CandidateIdsReadStringsAndNumbersInOrderWithoutDuplicates()
        {
            var tests = JsonNode.Parse("""["29913", 29914, "29913", "x", null, "0", "29915", "29916"]""");

            Assert.Equal([29913L, 29914L, 29915L], ExamPageSamples.CandidateIds(tests, 3));
            Assert.Empty(ExamPageSamples.CandidateIds(JsonNode.Parse("\"\""), 3));
            Assert.Empty(ExamPageSamples.CandidateIds(null, 3));
        }

        [Fact]
        public void PaperIdReadsGamaApisStringOrNothing()
        {
            Assert.Equal(31260, ExamPageSamples.PaperId(JsonNode.Parse("""{"paperID":"31260"}""")!.AsObject()));
            Assert.Equal(0, ExamPageSamples.PaperId(JsonNode.Parse("""{"paperID":null}""")!.AsObject()));
            Assert.Equal(0, ExamPageSamples.PaperId(JsonNode.Parse("{}")!.AsObject()));
        }
    }
}
