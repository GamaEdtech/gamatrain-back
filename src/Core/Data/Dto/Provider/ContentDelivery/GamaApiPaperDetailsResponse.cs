namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Raw shape (the fields this feature actually uses - the real response carries many more
    /// test-metadata fields) of gama-api's GET /tests/{id} test-detail endpoint. Confirmed live
    /// 2026-07-20: reports the same per-file price/paid gama-api's download endpoint does, but
    /// side-effect-free - repeated calls never change `paid`, unlike a call to
    /// /tests/download/{id}/{type}, which legitimately flips it as a side effect of serving the
    /// file. This is what lets ContentDeliveryService check affordability before ever calling the
    /// download endpoint - see GamaApiContentDeliveryProvider.GetContentPriceStatusAsync.
    /// </summary>
    public sealed class GamaApiPaperDetailsResponse
    {
        [JsonPropertyName("files")]
        public GamaApiPaperDetailsFiles? Files { get; set; }
    }

    public sealed class GamaApiPaperDetailsFiles
    {
        [JsonPropertyName("pdf")]
        public GamaApiPaperDetailsFileStatus? Pdf { get; set; }

        [JsonPropertyName("word")]
        public GamaApiPaperDetailsFileStatus? Word { get; set; }

        [JsonPropertyName("answer")]
        public GamaApiPaperDetailsFileStatus? Answer { get; set; }
    }

    public sealed class GamaApiPaperDetailsFileStatus
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("paid")]
        public bool Paid { get; set; }
    }
}
