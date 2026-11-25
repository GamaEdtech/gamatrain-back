namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>One file of a past paper, for gama-api's temporary download link.</summary>
    public sealed class ExamImportPaperFileRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The signed-in caller here (from the MCP token), which the native source acts for.</summary>
        public long UserId { get; set; }

        public long PaperId { get; set; }

        /// <summary>The file's id on Gamatrain, which the native source reads it by.</summary>
        public long? FileId { get; set; }

        /// <summary>The file's role (question, word, answer or extra), which gama-api reads it by.</summary>
        public required string Type { get; set; }

        /// <summary>An extra file's id.</summary>
        public long? ExtraId { get; set; }
    }
}
