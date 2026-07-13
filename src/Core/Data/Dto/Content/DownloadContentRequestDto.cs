namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadContentRequestDto
    {
        public required long UserId { get; set; }

        /// <summary>The downloading user's own gama-api legacy JWT, read from the request's Authorization header - required because gama-api prices/gates the download per caller.</summary>
        public required string Token { get; set; }

        public required long Id { get; set; }

        /// <summary>Which content this is, and which gama-api endpoint to resolve it from - see ContentType.</summary>
        public required ContentType ContentType { get; set; }

        /// <summary>gama-api's own file-type discriminator: pdf/word/answer/extra. Required for PastPaper/Test only - unused for Multimedia/Exam.</summary>
        public string? FileType { get; set; }

        public long? ExtraId { get; set; }
    }
}
