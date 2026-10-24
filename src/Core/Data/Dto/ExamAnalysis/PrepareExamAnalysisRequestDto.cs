namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    public sealed class PrepareExamAnalysisRequestDto
    {
        public required long UserId { get; set; }
        public required long ExamId { get; set; }
    }
}
