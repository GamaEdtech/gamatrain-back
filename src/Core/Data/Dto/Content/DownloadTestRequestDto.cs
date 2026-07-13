namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class DownloadTestRequestDto
    {
        public required long UserId { get; set; }

        /// <summary>The downloading user's own gama-api legacy JWT, read from the request's Authorization header - required because gama-api prices/gates the download per caller.</summary>
        public required string Token { get; set; }

        public required long Id { get; set; }

        /// <summary>gama-api's own file-type discriminator: pdf/word/answer/extra.</summary>
        public required string FileType { get; set; }

        public long? ExtraId { get; set; }

        /// <summary>Which of our own points/quota features this download is charged against (PastPaper or Test).</summary>
        public required ContentType ContentType { get; set; }
    }
}
