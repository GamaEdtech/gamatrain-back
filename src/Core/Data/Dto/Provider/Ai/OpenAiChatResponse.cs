namespace GamaEdtech.Data.Dto.Provider.Ai
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.HttpProvider;

    public sealed class OpenAiChatResponse : IHttpResponse
    {
        [JsonPropertyName("choices")]
        public IEnumerable<OpenAiChatChoice>? Choices { get; set; }

        /// <summary>Set instead of <see cref="Choices"/> when the request fails (bad key, quota, invalid model...).</summary>
        [JsonPropertyName("error")]
        public OpenAiChatError? Error { get; set; }
    }

    public sealed class OpenAiChatChoice
    {
        [JsonPropertyName("message")]
        public OpenAiChatMessage? Message { get; set; }
    }

    public sealed class OpenAiChatError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
