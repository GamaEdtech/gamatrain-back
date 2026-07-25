namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class GetContentPriceStatusRequestDto
    {
        /// <summary>The downloading user's own credential against the content source - price/paid is reported per caller.</summary>
        public required string Token { get; set; }
        public required long ExternalContentId { get; set; }

        /// <summary>Selects which gama-api detail endpoint to call: PastPaper -> /tests/{id}, Multimedia -> /files/{id}, Exam -> /exams/{id}.</summary>
        public required DownloadContentType ContentType { get; set; }

        /// <summary>Required only for PastPaper (gama-api's own pdf/word/answer discriminator - "extra" is not reported by /tests/{id}, callers should not call this method for it). Unused for Multimedia/Exam.</summary>
        public string? FileType { get; set; }
    }
}
