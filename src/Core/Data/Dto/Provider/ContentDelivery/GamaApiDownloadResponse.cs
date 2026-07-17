namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Shared raw shape for gama-api's three download-URL endpoints. /tests/download returns the
    /// full shape (OwnerUID + Price); /files/download and /exams/download only ever return
    /// {url, name} - OwnerUID/Price are left null by System.Text.Json when absent from the JSON,
    /// which is exactly what GamaApiContentDeliveryProvider relies on to skip charging/commission
    /// for those two endpoints.
    /// </summary>
    public sealed class GamaApiDownloadResponse
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("ownerUID")]
        public string? OwnerUID { get; set; }

        [JsonPropertyName("price")]
        public GamaApiDownloadPrice? Price { get; set; }
    }

    public sealed class GamaApiDownloadPrice
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("paid")]
        public bool Paid { get; set; }
    }
}
