namespace GamaEdtech.Data.Dto.Provider.Ai
{
    /// <summary>
    /// A provider-neutral, single-turn chat completion: optional system instructions plus one user message.
    /// </summary>
    public sealed class ChatCompletionRequestDto
    {
        public string? SystemPrompt { get; set; }
        public required string UserPrompt { get; set; }

        /// <summary>Upper bound on the generated tokens; the provider's configured default when null.</summary>
        public int? MaxTokens { get; set; }

        public double? Temperature { get; set; }
    }
}
