namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    using GamaEdtech.Data.Dto.Game;

    public sealed class PrepareExamAnalysisResponseDto
    {
        public ExamAnalysisDto? Analysis { get; set; }
        public SpendPointsResponseDto? Charge { get; set; }
    }
}
