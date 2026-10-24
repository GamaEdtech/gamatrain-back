namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    public sealed class ExamAnalysisDto
    {
        public long Id { get; set; }
        public long ExamId { get; set; }
        public string? Result { get; set; }
        public long Points { get; set; }
    }
}
