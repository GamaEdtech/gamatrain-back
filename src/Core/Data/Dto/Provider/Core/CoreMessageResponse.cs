namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Text.Json.Serialization;

    public sealed class CoreMessageResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
