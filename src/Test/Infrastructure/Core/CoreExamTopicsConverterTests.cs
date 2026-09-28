namespace GamaEdtech.Test.Infrastructure.Core
{
    using System.Text.Json;

    using GamaEdtech.Data.Dto.Provider.Core;

    using Xunit;

    /// <summary>Pure in-memory tests of gama-api's <c>exams/{id}</c> <c>topics</c> field, which is a string or an array.</summary>
    public class CoreExamTopicsConverterTests
    {
        [Fact]
        public void ArrayIsRead()
        {
            var exam = JsonSerializer.Deserialize<CoreExamResponse>("""{"title":"x","topics":[{"id":"9041","order":"47","title":" Unit 18: Position and direction","season":false,"pages":{"from":"","to":""}}]}""");

            var topic = Assert.Single(exam!.Topics!);
            Assert.Equal("9041", topic.Id);
            Assert.Equal(" Unit 18: Position and direction", topic.Title);
        }

        [Fact]
        public void NonEmptyStringIsOneTitle()
        {
            var exam = JsonSerializer.Deserialize<CoreExamResponse>("""{"title":"x","topics":" Chapter 3: Dynamics "}""");

            Assert.Equal("Chapter 3: Dynamics", Assert.Single(exam!.Topics!).Title);
        }

        [Theory]
        [InlineData("""{"title":"x","topics":""}""")]
        [InlineData("""{"title":"x","topics":"6072"}""")]
        [InlineData("""{"title":"x","topics":"6072,6073"}""")]
        [InlineData("""{"title":"x","topics":null}""")]
        [InlineData("""{"title":"x","topics":{}}""")]
        [InlineData("""{"title":"x"}""")]
        public void NoTitlesIsNullAndTheRestStillReads(string json)
        {
            var exam = JsonSerializer.Deserialize<CoreExamResponse>(json);

            Assert.Null(exam!.Topics);
            Assert.Equal("x", exam.Title);
        }
    }
}
