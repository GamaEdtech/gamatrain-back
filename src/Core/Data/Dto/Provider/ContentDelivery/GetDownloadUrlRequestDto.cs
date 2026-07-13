namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class GetDownloadUrlRequestDto
    {
        /// <summary>The downloading user's own credential against the content source (e.g. a gama-api legacy JWT) - the source authorizes/prices per caller, so this can't be a service-level credential.</summary>
        public required string Token { get; set; }
        public required long ExternalContentId { get; set; }

        /// <summary>Selects which gama-api download endpoint to call: PastPaper/Test -> /tests/download, Multimedia -> /files/download, Exam -> /exams/download.</summary>
        public required ContentType ContentType { get; set; }

        /// <summary>Required only for PastPaper/Test (gama-api's own pdf/word/answer/extra discriminator) - unused for Multimedia/Exam.</summary>
        public string? FileType { get; set; }
        public long? ExtraId { get; set; }
    }
}
