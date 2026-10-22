namespace GamaEdtech.Data.Dto.Provider.Ai
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.HttpProvider;

    public sealed class OpenAiChatRequest : IHttpRequest
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("messages")]
        public IEnumerable<OpenAiChatMessage>? Messages { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; set; }
    }

    public sealed class OpenAiChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
