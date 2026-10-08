namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A draft exam on gama-api (status 6): the one an upload creates, or the caller's existing one.</summary>
    public sealed class ExamImportDraftDto
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        public string? Title { get; set; }
    }
}
