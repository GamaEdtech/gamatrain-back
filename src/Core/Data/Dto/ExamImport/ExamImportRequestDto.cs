namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A gama-api exam-builder call about one item (an exam, a question or a paper), with the caller's token.</summary>
    public sealed class ExamImportRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        public long Id { get; set; }
    }
}
