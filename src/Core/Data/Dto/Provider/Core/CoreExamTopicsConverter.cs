namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// gama-api's <c>exams/{id}</c> <c>topics</c> comes in two shapes: an array of topic objects (exams 2050, 2037,
    /// 831) or a string, <c>""</c> when the exam has none (exam 1061). An array is read as is; a non-empty string is
    /// taken as one topic title, unless it's only ids (digits/commas, like examTests' own <c>topics</c>), which have
    /// no title to show; anything else is <see langword="null"/>. Never fails the whole exam response.
    /// </summary>
    public sealed class CoreExamTopicsConverter : JsonConverter<Collection<CoreExamTopic>?>
    {
        public override Collection<CoreExamTopic>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    return JsonSerializer.Deserialize<Collection<CoreExamTopic>>(ref reader, options);
                case JsonTokenType.String:
                    var text = reader.GetString()?.Trim();
                    return string.IsNullOrEmpty(text) || text.All(t => char.IsDigit(t) || t is ',' or ' ')
                        ? null
                        : [new() { Title = text }];
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write([NotNull] Utf8JsonWriter writer, Collection<CoreExamTopic>? value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
