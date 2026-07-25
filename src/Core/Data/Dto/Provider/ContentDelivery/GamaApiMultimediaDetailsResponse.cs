namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Raw shape (the fields this feature actually uses) of gama-api's GET /files/{id} multimedia
    /// detail endpoint - side-effect-free, same role as GamaApiPaperDetailsResponse but for
    /// Multimedia. Unlike tests, a file has exactly one price/paid pair (no pdf/word/answer split).
    /// </summary>
    public sealed class GamaApiMultimediaDetailsResponse
    {
        [JsonPropertyName("files")]
        public GamaApiMultimediaDetailsStatus? Files { get; set; }
    }

    public sealed class GamaApiMultimediaDetailsStatus
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("paid")]
        public bool Paid { get; set; }
    }
}
