namespace GamaEdtech.Test.Infrastructure
{
    using System.Text.Json;

    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Infrastructure.Provider.Core;

    using Xunit;

    public sealed class CoreExamResultDetailMappingTests
    {
        // Shaped like a real gama-api exams/result/{id} response (2026-10-26): numbers as text, "" for unanswered,
        // "0" for no image, submit_time capped at 65535, submit_time_seconds as Persian text, rank without positions.
        private const string Response = """
            {
              "status": 1,
              "data": {
                "user": { "id": "1", "first_name": "Sara", "last_name": "Example", "school": "A school" },
                "exam": { "title": "Biology Online Exam", "azmoon_time": "45", "score_type": "1" },
                "userData": { "submit_time": "65535", "submit_time_seconds": "18 ساعت و 12 دقیقه و 15 ثانیه" },
                "rank": { "fields": ["score"] },
                "answerStats": {
                  "total": { "num": 2, "true": 0, "false": 1, "noAnswer": 1, "percent": 0 },
                  "lessons": { "2204": { "title": "Biology", "num": 2, "true": 0, "false": 1, "noanswer": 1, "percent": 0 } }
                },
                "result": [
                  {
                    "question": "<p>Earthworms come to the surface when...</p>", "type": "fourchoice",
                    "answer_a": "<p>growth</p>", "answer_b": "b", "answer_c": "c", "answer_d": "d",
                    "q_file": null, "a_file": "0", "b_file": null, "c_file": null, "d_file": null,
                    "true_answer": "4", "user_answer": "2", "lesson_title": "Biology (0610)", "topics_title": "UNIT 1"
                  },
                  {
                    "question": "<p>Which parts make up the name?</p>", "type": "fourchoice",
                    "answer_a": "genus and species", "answer_b": "b", "answer_c": "c", "answer_d": "d",
                    "true_answer": 1, "user_answer": ""
                  }
                ]
              }
            }
            """;

        [Fact]
        public void RealResponseShapeIsMapped()
        {
            var response = JsonSerializer.Deserialize<CoreResponse<CoreExamResultDetailResponse>>(Response, JsonSerializerOptions.Web)!;

            var result = CoreProvider.MapExamResultDetail(response.Data!, response.Data!.AnswerStats!.Total!);

            Assert.Equal(2, result.Stats.Total);
            Assert.Equal(1, result.Stats.NoAnswer);
            Assert.Equal("Sara", result.StudentFirstName);
            Assert.Equal("Biology Online Exam", result.ExamTitle);
            Assert.Equal("45", result.ExamTime);
            Assert.Null(result.TimeTakenSeconds);
            Assert.Empty(result.Ranks);
            Assert.Equal(2, result.Questions.Count);

            var first = result.Questions[0];
            Assert.Equal('D', first.CorrectOption);
            Assert.Equal('B', first.StudentOption);
            Assert.Null(first.Options[0].File);
            Assert.Equal("UNIT 1", first.Topics);
            Assert.False(first.IsDescriptive);

            var second = result.Questions[1];
            Assert.Equal('A', second.CorrectOption);
            Assert.Null(second.StudentOption);
        }

        [Fact]
        public void TimeTakenAndRanksAreReadWhenPresent()
        {
            const string json = """
                {
                  "userData": { "submit_time": "725" },
                  "rank": { "total": { "total": "120", "user": 3 }, "state": { "total": 40, "user": "1" }, "area": [] },
                  "answerStats": { "total": { "num": 0, "true": 0, "false": 0, "noAnswer": 0, "percent": 0 } }
                }
                """;
            var data = JsonSerializer.Deserialize<CoreExamResultDetailResponse>(json, JsonSerializerOptions.Web)!;

            var result = CoreProvider.MapExamResultDetail(data, data.AnswerStats!.Total!);

            Assert.Equal(725, result.TimeTakenSeconds);
            Assert.Equal(2, result.Ranks.Count);
            Assert.Equal(new("total", 3, 120), result.Ranks[0]);
            Assert.Equal(new("state", 1, 40), result.Ranks[1]);
        }

        [Fact]
        public void EmptyRankArrayIsAccepted()
        {
            const string json = """{ "rank": [], "answerStats": { "total": { "num": 0, "true": 0, "false": 0, "noAnswer": 0, "percent": 0 } } }""";
            var data = JsonSerializer.Deserialize<CoreExamResultDetailResponse>(json, JsonSerializerOptions.Web)!;

            Assert.Empty(CoreProvider.MapExamResultDetail(data, data.AnswerStats!.Total!).Ranks);
        }
    }
}
