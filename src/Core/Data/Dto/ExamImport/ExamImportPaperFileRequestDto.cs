namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>One file of a past paper, for gama-api's temporary download link.</summary>
    public sealed class ExamImportPaperFileRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        public long PaperId { get; set; }

        /// <summary>pdf, word, answer or extra.</summary>
        public required string Type { get; set; }

        /// <summary>An extra file's id.</summary>
        public long? ExtraId { get; set; }
    }
}
