namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    public sealed class SaveExamAnalysisResultRequestDto
    {
        public required long UserId { get; set; }
        public required long ExamId { get; set; }
        public required string Result { get; set; }
    }
}
