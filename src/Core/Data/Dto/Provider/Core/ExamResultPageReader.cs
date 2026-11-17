namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.Game;

    /// <summary>
    /// Reads a result page in gama-api's <c>exams/result/{id}</c> shape into <see cref="ExamResultDetailResponseDto"/> (what the
    /// points and the AI analysis use). Shared by every <c>IExamSource</c>: gama-api's page and the native one have the same shape.
    /// </summary>
    public static class ExamResultPageReader
    {
        /// <summary>The page's result, or null when it holds no result (no <c>answerStats.total</c>).</summary>
        public static ExamResultDetailResponseDto? Read([NotNull] JsonObject page)
        {
            var data = page.Deserialize<CoreExamResultDetailResponse>(JsonSerializerOptions.Web);
            return data?.AnswerStats?.Total is { } total ? Map(data, total) : null;
        }

        public static ExamResultDetailResponseDto Map([NotNull] CoreExamResultDetailResponse data, [NotNull] CoreExamResultResponse.AnswerStatsDto.TotalDto total) => new()
        {
            Stats = new()
            {
                Invalid = total.False,
                Valid = total.True,
                NoAnswer = total.NoAnswer,
                Total = total.Num,
                Percent = total.Percent,
            },
            StudentFirstName = JsonText(data.User?.FirstName),
            ExamTitle = JsonText(data.Exam?.Title),
            ExamTime = JsonText(data.Exam?.ExamTime),
            // gama-api's figure only when believable (see ExamTiming); a time this backend recorded at submit replaces it.
            TimeTakenSeconds = ExamTiming.FromGamaApi(JsonInt(data.UserData?.SubmitTime), ExamTiming.ParseWhole(JsonText(data.Exam?.ExamTime))),
            Ranks = ReadRanks(data.Rank),
            Questions = [.. (data.Result ?? []).Select(t => new ExamResultDetailResponseDto.QuestionDto
            {
                Question = JsonText(t.Question),
                QuestionFile = FileUrlOrNull(JsonText(t.QuestionFile)),
                Type = JsonText(t.Type),
                Options =
                [
                    new(JsonText(t.OptionA), FileUrlOrNull(JsonText(t.OptionAFile))),
                    new(JsonText(t.OptionB), FileUrlOrNull(JsonText(t.OptionBFile))),
                    new(JsonText(t.OptionC), FileUrlOrNull(JsonText(t.OptionCFile))),
                    new(JsonText(t.OptionD), FileUrlOrNull(JsonText(t.OptionDFile))),
                ],
                CorrectOption = AnswerLetter(JsonInt(t.TrueAnswer)),
                StudentOption = AnswerLetter(JsonInt(t.UserAnswer)),
                Lesson = JsonText(t.LessonTitle),
                Topics = JsonText(t.TopicsTitle),
            })],
        };

        // gama-api's 1-4 answer numbers; 0, empty or anything else is no option.
        private static char? AnswerLetter(int? value) => value is >= 1 and <= 4 ? (char)('A' + value.Value - 1) : null;

        // rank: {"total": {"total": "120", "user": 5}, "state": {...}, "area": {...}}; it can also be [] or hold none of these.
        private static List<ExamResultDetailResponseDto.RankDto> ReadRanks(JsonElement? rank)
        {
            if (rank is not { ValueKind: JsonValueKind.Object } value)
            {
                return [];
            }

            List<ExamResultDetailResponseDto.RankDto> ranks = [];
            foreach (var scope in new[] { "total", "state", "area" })
            {
                if (value.TryGetProperty(scope, out var item) && item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("user", out var user) && JsonInt(user) is int position && position > 0)
                {
                    ranks.Add(new(scope, position, item.TryGetProperty("total", out var participants) ? JsonInt(participants) : null));
                }
            }

            return ranks;
        }

        private static string? JsonText(JsonElement? value) => value?.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.Value.GetString()) ? null : value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };

        private static int? JsonInt(JsonElement? value) => value?.ValueKind switch
        {
            JsonValueKind.Number when value.Value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };

        // gama-api sends "0" (not null) in its file fields for "no image".
        private static string? FileUrlOrNull(string? value) => string.IsNullOrWhiteSpace(value) || value == "0" ? null : value;
    }
}
