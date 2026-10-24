namespace GamaEdtech.Data.Dto.Exam
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Security;
    using GamaEdtech.Data.Dto.Provider.Core;

    /// <summary>The rules for the exam page's sample questions, kept pure so they can be tested without gama-api.</summary>
    public static class ExamPageSamples
    {
        /// <summary>The first <paramref name="count"/> question ids of gama-api's exam <c>tests</c> (strings or numbers),
        /// in exam order, without duplicates or unusable values.</summary>
        public static IReadOnlyList<long> CandidateIds(JsonNode? tests, int count) => tests is JsonArray array
            ? [.. array.Select(ToId).Where(id => id > 0).Distinct().Take(count)]
            : [];

        /// <summary>The exam's source paper id (gama-api's <c>paperID</c>), or 0 when it has none.</summary>
        public static long PaperId(JsonObject? exam) => ToId(exam?["paperID"]);

        /// <summary>The first <paramref name="count"/> usable questions, keeping the order they were passed in.</summary>
        public static IReadOnlyList<ExamSampleQuestionDto> Select(IEnumerable<CoreExamTestResponse?> testsInOrder, int count) =>
            [.. testsInOrder.Select(Map).OfType<ExamSampleQuestionDto>().Take(count)];

        /// <summary>A question for the public page, or <see langword="null"/> when there's nothing to show (missing, locked by
        /// gama-api's paywall, or no text and no image). Only the question and its options are copied: never
        /// <see cref="CoreExamTestResponse.TrueAnswer"/> or the worked answer.</summary>
        public static ExamSampleQuestionDto? Map(CoreExamTestResponse? test)
        {
            if (test is null || test.Paywall?.Locked == true || !long.TryParse(test.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return null;
            }

            var question = test.Question.SanitizeHtml()?.Trim();
            var image = ImageUrlOrNull(test.QuestionFile);
            if (string.IsNullOrEmpty(question) && image is null)
            {
                return null;
            }

            ExamSampleOptionDto[] options =
            [
                Option(1, test.OptionA, test.OptionAFile),
                Option(2, test.OptionB, test.OptionBFile),
                Option(3, test.OptionC, test.OptionCFile),
                Option(4, test.OptionD, test.OptionDFile),
            ];

            return new()
            {
                Id = id,
                Code = string.IsNullOrWhiteSpace(test.Code) ? null : test.Code.Trim(),
                Question = question,
                Image = image,
                Options = [.. options.Where(o => !string.IsNullOrEmpty(o.Text) || o.Image is not null)],
            };
        }

        private static ExamSampleOptionDto Option(int key, string? text, string? file) => new()
        {
            Key = key,
            Text = text.SanitizeHtml()?.Trim(),
            Image = ImageUrlOrNull(file),
        };

        /// <summary>gama-api sends files as full URLs, with "" or "0" for none. Anything that isn't an http(s) URL is dropped.</summary>
        private static string? ImageUrlOrNull(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? value
                : null;

        private static long ToId(JsonNode? node) => node is not JsonValue value
            ? 0
            : value.GetValueKind() switch
            {
                JsonValueKind.Number when value.TryGetValue<long>(out var number) => number,
                JsonValueKind.String when long.TryParse(value.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0,
            };
    }
}
