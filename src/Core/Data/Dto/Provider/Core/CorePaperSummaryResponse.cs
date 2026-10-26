namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json.Serialization;

    /// <summary>The few fields of gama-api's public <c>GET tests/{id}</c> (config <c>Core:TestDetails</c>) needed to link
    /// an exam to the past paper it was made from.</summary>
    public sealed class CorePaperSummaryResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>The paper page's slug on gamatrain.com.</summary>
        [JsonPropertyName("title_url")]
        public string? TitleUrl { get; set; }

        /// <summary>True for a real past paper; false for other items in the same list, like a topical question set.</summary>
        [JsonPropertyName("is_paper")]
        public bool IsPaper { get; set; }

        /// <summary>"1" when published.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}
