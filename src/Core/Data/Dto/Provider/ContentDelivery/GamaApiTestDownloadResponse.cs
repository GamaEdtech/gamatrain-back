namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using System.Text.Json.Serialization;

    public sealed class GamaApiTestDownloadResponse
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("ownerUID")]
        public string? OwnerUID { get; set; }

        [JsonPropertyName("price")]
        public GamaApiTestDownloadPrice? Price { get; set; }
    }

    public sealed class GamaApiTestDownloadPrice
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("paid")]
        public bool Paid { get; set; }
    }
}
